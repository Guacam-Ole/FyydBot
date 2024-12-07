using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FyydBot;


public class FyydPodcastResponse
{
    public FyydPodcast Data { get; set; }
}

public class FyydPodcast
{
    public string Title { get; set; }
    public int Id { get; set; }
}

public class FyydSearchResponse
{
    [JsonPropertyName("status")]
    public int StatusCode { get; set; }
    [JsonPropertyName("msg")]
    public string StatusMessage { get; set; }
    public FyydSearchResponseMeta Meta { get; set; }
    public List<FyydResponseElement> Data { get; set; }
        
    
    public class FyydSearchResponseMeta
    {
        [JsonPropertyName("API_INFO")]
        public FyydResponseApiInfo ApiInfo { get; set; }
        public string Server  { get; set; }
        public int? Duration { get; set; }
    }
    
    public class FyydResponseApiInfo
    {
        [JsonPropertyName("API_VERSION")]
        public string Version { get; set; }
    }
    
    public class FyydResponseElement
    {
        public string? PodcastName { get; set; } 
        public string Title { get; set; }
        public int Id { get; set; }
        public string Guid { get; set; }
        public string Url { get; set; }
        public string Enclosure { get; set; }
        [JsonPropertyName("podcast_id")]
        public int PodcastId { get; set; }
        [JsonPropertyName("imgURL")]
        public string ImageUrl { get; set; }
        [JsonPropertyName("pubdate")]
        public DateTime Published { get; set; }
        public int? Duration { get; set; }
        public int? Status { get; set; }
        [JsonPropertyName("num_season")]
        public int? Season { get; set; }
        [JsonPropertyName("num_episode")]
        public int? Episode { get; set; }
        [JsonPropertyName("inserted")]
        public DateTime Created { get; set; }
        [JsonPropertyName("url_fyyd")]
        public string FyydUrl { get; set; }
        public string Description { get; set; }
        [JsonPropertyName("duration_string")]
        public string DurationString { get; set; }
        [JsonPropertyName("content_type")]
        public string ContentType { get; set; }
    }
}

