using Mastonet;
using Mastonet.Entities;
using Microsoft.Extensions.Logging;

namespace FyydBot;

public class Mastodon
{
    private readonly Secrets _secrets;
    private readonly ILogger<MastodonClient> _logger;
    private const string FyydUrlPrefix = "https://fyyd.de/search?search=";
    private const string FyydEpisodeUrlPrefix = "https://fyyd.de/episode";
    private const int EpisodeResultMaxDisplayCount = 5;
    private const int EpisodeResultMaxMessageLength = 450;
    private const int CharLengthForSingleUrl = 16;
    private const int maxMastodonRetries = 5;

    // https://fyyd.de/search?search=horido&ext_min_date=2024-12-03&ext_max_date=2024-12-21
    private const int MaxLength = 470; // 500 -url
    private const string PlaceHolder = "[PODCASTS]";

    public Mastodon(Secrets secrets, ILogger<MastodonClient> logger)
    {
        _secrets = secrets;
        _logger = logger;
    }

    private static MastodonClient Login(string instance, string accessToken)
    {
        return new MastodonClient(instance, accessToken);
    }


    private static string? FillMessageWithPodcastNames(string message, List<string?> podcastNames)
    {
        switch (podcastNames.Count)
        {
            case 0:
                return null;
            case 1:
                return message.Replace(PlaceHolder, $"{podcastNames.First()}");
        }

        var allButOne = podcastNames.Take(podcastNames.Count - 1).Select(pn => $"'{pn}'").ToList();
        return message.Replace(PlaceHolder, string.Join(", ", allButOne));
    }

    private string? BuildResponseString(string message, List<string?> podcastNames, int maxLength)
    {
        var distinctNames = podcastNames.Where(q => !string.IsNullOrWhiteSpace(q)).Distinct().ToList();
        var completeMessage = FillMessageWithPodcastNames(message, distinctNames);
        if (completeMessage == null) return null;

        while (distinctNames.Count > 0 && completeMessage.Length > maxLength)
        {
            distinctNames.Remove(distinctNames.Last());
            completeMessage = FillMessageWithPodcastNames(message, distinctNames);
            if (completeMessage == null) return null;
        }

        if (distinctNames.Count == 0) completeMessage = message.Replace(PlaceHolder, "mehreren");
        return completeMessage;
    }

    public async Task SendErrorResponse(Status replyTo, string id, string error)
    {
        await UpdateSingleMessage(replyTo.Account, id, error);
        _logger.LogWarning("Sent error message to '{recipient}':'{message}'", replyTo.Account.AccountName, error);
    }


    public async Task<string> ReplyTo(Status replyTo, string message)
    {
        var id = await SendSingleMessage(replyTo.Id, replyTo.Visibility, replyTo.GetUserHandle() + message);
        return id;
    }

    public async Task CreateResponseFromSearchResult(Status replyTo, string id, LlamaResponseQuery llamaQuery,
        List<FyydSearchResponse.FyydResponseElement> episodes)
    {
        var podcastIds = episodes.Where(episode => episode.PodcastId > 0).DistinctBy(episode => episode.PodcastId)
            .ToList();

        var query = FyydUrlPrefix;
        if (podcastIds.Count == 1)
        {
            query += $"podcast_id:{podcastIds.First().PodcastId}%20";
        }
        else if (!string.IsNullOrWhiteSpace(llamaQuery.PodcastName))
        {
            query += $"{llamaQuery.PodcastName.Escape()}%20";
        }

        if (!string.IsNullOrWhiteSpace(llamaQuery.Keywords)) query += $"{llamaQuery.Keywords.Escape()}%20";
        query = query.Trim();
        if (llamaQuery.DateRange?.StartDateValue != null)
        {
            query += $"&ext_min_date={llamaQuery.DateRange.StartDateValue.Value:yyyy-MM-dd}";
        }

        if (llamaQuery.DateRange?.EndDateValue != null)
        {
            query += $"&ext_max_date={llamaQuery.DateRange.EndDateValue.Value:yyyy-MM-dd}";
        }

        var humanReadableResponse = query;

        var countStr = episodes.Count switch
        {
            > 20 => "mehr als 20",
            _ => episodes.Count.ToString()
        };

        var message =
            BuildResponseString(
                $"Ich habe {countStr} Folgen gefunden u.a. bei:\n\n{PlaceHolder}\n\n{humanReadableResponse}",
                episodes.Select(episode => episode.PodcastName).ToList(), MaxLength);
        if (message == null)
        {
            await UpdateSingleMessage(replyTo.Account, id, "Sorry. Da ist was schief gelaufen");
            _logger.LogError("Cannot build message. Sent error to Mastodon");
            return;
        }

        var newId = await ReplyTo(replyTo, message);
        var episodeMessage = BuildTopList(episodes);
        if (episodeMessage != null)
        {
            await SendSingleMessage(newId, replyTo.Visibility, replyTo.GetUserHandle() + episodeMessage);
        }
    }

    private string? BuildTopList(List<FyydSearchResponse.FyydResponseElement> episodes)
    {
        var topList = "";
        string message;
        var realDisplayCount = 0;
        foreach (var episode in episodes.OrderByDescending(episode => episode.Published)
                     .Take(EpisodeResultMaxDisplayCount))
        {
            var singleEpisodeText = $"Podcast: {episode.PodcastName}\nEpisode: {episode.Title}\nUrl: ";
            if (topList.Length + singleEpisodeText.Length + CharLengthForSingleUrl > EpisodeResultMaxMessageLength)
                break; // not enough space
            topList += singleEpisodeText + $"{FyydEpisodeUrlPrefix}/{episode.Id}\n\n";
            realDisplayCount++;
        }

        if (realDisplayCount == 0)
        {
            _logger.LogWarning("Cannot even display a single episode because of length constraints");
            return null;
        }


        if (realDisplayCount == 1)
        {
            message = "Das ist die neueste gefundene Episode:\n\n" + topList;
        }
        else if (realDisplayCount == episodes.Count)
        {
            message = "Das sind die gefundenen Episoden:\n\n" + topList;
        }
        else
        {
            message = $"Das sind die {realDisplayCount} neuesten gefunden Episoden:\n\n" + topList;
        }

        return message;
    }


    private async Task Retry(Func<Task> func)
    {
        var retriesLeft = maxMastodonRetries;
        while (retriesLeft > 0)
        {
            try
            {
                await func();
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on Mastodon call {retries} retries left", retriesLeft);
                Thread.Sleep((1+maxMastodonRetries-retriesLeft)*10000); // wait 10,20,30,... seconds
                retriesLeft--;
                if (retriesLeft == 0) throw;
            }
        }

        throw new Exception("no retry left");
    }

    private async Task<TResult> Retry<TResult>(Func<Task<TResult>> func)
    {
        var retriesLeft = maxMastodonRetries;
        while (retriesLeft > 0)
        {
            try
            {
                return await func();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on Mastodon call {retries} retries left", retriesLeft);
                Thread.Sleep((1+maxMastodonRetries-retriesLeft)*10000); // wait 10,20,30,... seconds
                retriesLeft--;
                if (retriesLeft == 0) throw;
            }
        }

        throw new Exception("no retry left");
    }

    private async Task<string> SendSingleMessage(string replyToId, Visibility visibility, string message)
    {
        return await Retry(async () =>
        {
            _logger.LogDebug("sending single message");
            var client = Login(_secrets.Mastodon.Instance, _secrets.Mastodon.AccessToken);
            var status = await client.PublishStatus(message, visibility, replyToId);
            _logger.LogInformation("Added message to status: '{id}' in reply to '{reply}'", status.Id, replyToId);
            return status.Id;
        });
    }

    public async Task DismissNotification(string notificationId)
    {
        await Retry(async () =>
        {
            _logger.LogDebug("dismissing notification");
            var client = Login(_secrets.Mastodon.Instance, _secrets.Mastodon.AccessToken);
            await client.DismissNotification(notificationId);
        });
    }

    public async Task UpdateSingleMessage(Account account, string id, string message)
    {
        await Retry(async () =>
        {
            _logger.LogDebug("updated single message");
            message = "@" + account.AccountName + " " + message;
            var client = Login(_secrets.Mastodon.Instance, _secrets.Mastodon.AccessToken);
            return await client.EditStatus(id, message);
        });
    }

    public async Task<List<Notification>> GetUnreadNotifications()
    {
        return await Retry(async () =>
        {
            var client = Login(_secrets.Mastodon.Instance, _secrets.Mastodon.AccessToken);
            var notifications = await client.GetNotifications();
            return notifications.Where(notification => notification.Type == "mention").ToList();
        });
    }
}