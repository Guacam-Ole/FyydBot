using System.Net;
using System.Text.RegularExpressions;
using Mastonet;
using Mastonet.Entities;
using Microsoft.Extensions.Logging;

namespace FyydBot;

public class Mastodon
{
    private readonly Secrets _secrets;
    private readonly ILogger<MastodonClient> _logger;
    private const string _fyydUrlPrefix = "https://fyyd.de/search?search=";
    
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

    public async Task<List<Notification>> GetUnreadNotifications()
    {
        var client = Login(_secrets.Mastodon.Instance, _secrets.Mastodon.AccessToken);
        var notifications = await client.GetNotifications();
        return notifications.Where(notification => notification.Type == "mention" ).ToList();
    }

    public async Task DismissNotification(string notificationId)
    {
        var client = Login(_secrets.Mastodon.Instance, _secrets.Mastodon.AccessToken);
        await client.DismissNotification(notificationId);
    }

    public string StripHtml(string content)
    {
        content = content.Replace("</p>", " \n\n");
        content = content.Replace("<br />", " \n");
        content = Regex.Replace(content, "<[a-zA-Z/].*?>", String.Empty);
        content = WebUtility.HtmlDecode(content);
        return content;
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

    public async Task SendErrorResponse(Status replyTo, Visibility visibility, string error)
    {
        var user = "@" + replyTo.Account.AccountName + " ";
        await SendSingleMessage(replyTo.Id, visibility, user + error);
        _logger.LogWarning("Sent error message to '{recipient}':'{message}'", user, error);
    }

    public async Task CreateResponseFromSearchResult(Status replyTo, LlamaResponseQuery llamaQuery, Visibility visibility, List<FyydSearchResponse.FyydResponseElement> episodes)
    {
        var user = "@" + replyTo.Account.AccountName + " ";

        var query = _fyydUrlPrefix;
        if (!string.IsNullOrWhiteSpace(llamaQuery.PodcastName)) query += $"{llamaQuery.PodcastName} ";
        if (!string.IsNullOrWhiteSpace(llamaQuery.Keywords)) query += $"{llamaQuery.Keywords} ";
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
            1 => "eine",
            _ => episodes.Count.ToString()
        };

        var message = BuildResponseString($"{user}Ich habe {countStr} Folgen gefunden u.a. bei:\n\n{PlaceHolder}\n\n{humanReadableResponse}", episodes.Select(episode => episode.PodcastName).ToList(), MaxLength);
        if (message == null)
        {
            await SendSingleMessage(replyTo.Id, visibility, "Sorry. Da ist was schiefgelaufen");
            _logger.LogError("Cannot build message. Sent error to Mastodon");
            return;
        }

        await SendSingleMessage(replyTo.Id, visibility, message);
    }

    private async Task<string> SendSingleMessage(string replyToId, Visibility visibility, string message)
    {
        var client = Login(_secrets.Mastodon.Instance, _secrets.Mastodon.AccessToken);
        var status = await client.PublishStatus(message, visibility, replyToId);
        _logger.LogInformation("Added message to status: '{id}' in reply to '{reply}'", status.Id, replyToId);
        return status.Id;
    }
}