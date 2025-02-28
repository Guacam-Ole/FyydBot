using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace FyydBot;

public class Fyyd
{
    private static readonly Dictionary<int, string?> PodcastNames = new();
    private readonly ILogger<Fyyd> _logger;
    private readonly HttpClient _client;
    private readonly List<string> _blackList = ["Podcast"];

    public Fyyd(ILogger<Fyyd> logger)
    {
        _logger = logger;
        _client = new HttpClient();
        _client.BaseAddress = new Uri(FyydUrl);
        _client.DefaultRequestHeaders.Accept.Clear();
        _client.DefaultRequestHeaders.Add("User-Agent", "Mastodon-FyydBot");
        _client.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    private const string FyydUrl = "https://api.fyyd.de/0.2/";
    private const string Query = "search/episode?";

    public async Task<List<FyydSearchResponse.FyydResponseElement>?> SearchForEpisode(LlamaResponseQuery llamaQuery)
    {
        try
        {
            if (llamaQuery.PodcastName != null)
            {
                foreach (var blacklistItem in _blackList)
                {
                    llamaQuery.PodcastName = llamaQuery.PodcastName.Replace(blacklistItem, "");
                }

                llamaQuery.PodcastName = llamaQuery.PodcastName.Trim();
            }


            var query = Query;
            if (!string.IsNullOrWhiteSpace(llamaQuery.PodcastName)) query += $"podcast_title={llamaQuery.PodcastName.Escape()}&";
            query += $"term={llamaQuery.Keywords?.Escape()}";
            _logger.LogDebug("Sending query '{query}' to Fyyd", query);
            var response = await _client.GetFromJsonAsync<FyydSearchResponse>(query);
            if (response == null) return null;
            var matches = response.Data;
            if (llamaQuery.DateRange?.StartDateValue != null)
            {
                matches = matches.Where(match => match.Published >= llamaQuery.DateRange.StartDateValue).ToList();
            }

            if (llamaQuery.DateRange?.EndDateValue != null)
            {
                matches = matches.Where(match => match.Published <= llamaQuery.DateRange.EndDateValue).ToList();
            }

            foreach (var episode in matches)
            {
                episode.PodcastName = await GetPodcastName(episode.PodcastId);
            }

            _logger.LogInformation("Found '{count}' episodes for search query: '{query}'", response.Data.Count,
                llamaQuery.Query);
            return matches;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "getting search results failed for query '{query}'",
                JsonConvert.SerializeObject(llamaQuery));
            return null;
        }
    }

    private async Task<string?> GetPodcastName(int podcastId)
    {
        if (PodcastNames.TryGetValue(podcastId, out var podcastName)) return podcastName;
        var podcastNameFromFyyd = await GetPodcastNameFromFyyd(podcastId);
        PodcastNames[podcastId] = podcastNameFromFyyd;
        return PodcastNames[podcastId];
    }

    private async Task<string?> GetPodcastNameFromFyyd(int podcastId)
    {
        var podcastInfo = await _client.GetFromJsonAsync<FyydPodcastResponse>("podcast?podcast_id=" + podcastId);
        var title = podcastInfo?.Data?.Id != podcastId ? null : podcastInfo?.Data?.Title;

        _logger.LogDebug("Received Name '{title}' for id '{id}'", title, podcastId);
        return title;
    }
}