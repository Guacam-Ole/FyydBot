using Microsoft.Extensions.Logging;
using Microsoft.Recognizers.Text.DateTime;
using Newtonsoft.Json;

namespace FyydBot;

using LLama.Common;
using LLama;
using LLama.Sampling;

public class Llama
{
    private readonly Config _config;
    private readonly ILogger<Llama> _logger;

    public Llama(Config config, ILogger<Llama> logger)
    {
        _config = config;
        _logger = logger;
    }

    private static ChatHistory DefineStructuredChatHistory()
    {
        var chatHistory = new ChatHistory();

        chatHistory.AddMessage(AuthorRole.System,
            "Extract podcast search criteria from the user query and return the response as JSON:");
        chatHistory.AddMessage(AuthorRole.System,
            "Do only parse the query and detect the fields. Do NOT try to create matching contents");
        chatHistory.AddMessage(AuthorRole.System,
            "Output format: {\"PodcastName\": \"[name]\", \"Keywords\": \"[keywords]\", \"Date\": \"[date]\"}\n");
        return chatHistory;
    }

    private static ChatHistory DefineDateHistory()
    {
        var chatHistory = new ChatHistory();
        chatHistory.AddMessage(AuthorRole.System, "Forget everything from before. This is a new task");
        chatHistory.AddMessage(AuthorRole.System, $"The current Date and time is '{DateTime.Now}' ");
        chatHistory.AddMessage(AuthorRole.System,
            "Interpret an input from the user that contains a date value. This can be of human readable format like 'last month', a valid datetime value, just a year or a month.");
        chatHistory.AddMessage(AuthorRole.System,
            "If the user just enter a year responst with a range from 1.1. until 31.12. of that year");
        chatHistory.AddMessage(AuthorRole.System,
            "  Try to determine the startdate and enddate of that entry. Make sure to get the correct date. Do not attempt to guess the year or the month if it is missing");

        chatHistory.AddMessage(AuthorRole.System,
            "Do only parse the query and detect the fields. Do NOT try to create matching contents");
        chatHistory.AddMessage(AuthorRole.System, "All dates should be in the format yyyy-MM-dd");
        chatHistory.AddMessage(AuthorRole.System,
            "Output format: {\"startDate\": \"[startDate]\", \"endDate\": \"[endDate]");
        return chatHistory;
    }

    private InferenceParams GetInferenceParams()
    {
        return new InferenceParams()
        {
            MaxTokens = 1024,
            AntiPrompts = new List<string> { "User:" },
            SamplingPipeline = new DefaultSamplingPipeline() { },
        };
    }

    private async Task<LlamaResponseQueryDates?> GetDates(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;

        // Try algorithm instead of ai first:
        var recognizeResults = DateTimeRecognizer.RecognizeDateTime(date, "de-de");
        if (recognizeResults != null && recognizeResults.Count != 0)
        {
            var firstResult = recognizeResults.First();
            if (firstResult.Resolution.TryGetValue("values", out var values))
            {
                var valueList = (List<Dictionary<string, string>>)values;
                if (valueList.Count > 0)
                {
                    valueList.First().TryGetValue("value", out var value);
                    valueList.First().TryGetValue("start", out var start);
                    valueList.First().TryGetValue("end", out var end);

                    if (start != null && end != null)
                    {
                        return new LlamaResponseQueryDates
                        {
                            StartDate = start,
                            EndDate = end
                        };
                    }

                    if (value != null)
                    {
                        var response = new LlamaResponseQueryDates { StartDate = start };
                        response.EndDate = response.StartDateValue.Value.AddDays(1).ToShortDateString();
                        return response;
                    }
                }
            }
        }

        try
        {
            var parameters = new ModelParams(_config.Gguf)
            {
                ContextSize = 1024,
                GpuLayerCount = 0,
            };
            using var model = LLamaWeights.LoadFromFile(parameters);
            using var context = model.CreateContext(parameters);
            var executor = new InteractiveExecutor(context);
            {
                ChatSession dateSession = new(executor, DefineDateHistory());
                var response = string.Empty;
                await foreach (
                    var text
                    in dateSession.ChatAsync(
                        new ChatHistory.Message(AuthorRole.User, date),
                        GetInferenceParams()))
                {
                    response += text;
                }

                var jsonStart = response.IndexOf('{');
                var jsonEnd = response.IndexOf('}', jsonStart) + 1;

                var json = response.Substring(jsonStart, jsonEnd - jsonStart);
                var dateResponse = JsonConvert.DeserializeObject<LlamaResponseQueryDates>(json);
                return dateResponse;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Cannot get dates from '{date}'", date);
            return null;
        }
    }

    public async Task<LlamaResponseQuery?> ParseSearchQuery(string search)
    {
        while (search.Contains("@"))
        {
            var atPos = search.IndexOf('@');
            var endAt = search.IndexOf(' ', atPos + 1);

            string fullName = null;
            fullName = endAt < 0
                ? search[atPos..]
                : search.Substring(atPos, endAt - atPos + 1);

            search = search.Replace(fullName, string.Empty);
        }

        search = search.Replace("\n", "").Trim();


        try
        {
            var now = DateTime.Now;
            var parameters = new ModelParams(_config.Gguf)
            {
                ContextSize = 1024,
                GpuLayerCount = 0,
            };
            using var model = LLamaWeights.LoadFromFile(parameters);
            using var context = model.CreateContext(parameters);
            var executor = new InteractiveExecutor(context);

            ChatSession querySession = new(executor, DefineStructuredChatHistory());

            var response = string.Empty;
            await foreach (
                var text
                in querySession.ChatAsync(
                    new ChatHistory.Message(AuthorRole.User, search),
                    GetInferenceParams()))
            {
                response += text;
            }

            _logger.LogDebug("it took '{time}' to parse search query", (DateTime.Now - now).TotalSeconds);
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.IndexOf('}', jsonStart) + 1;

            var json = response.Substring(jsonStart, jsonEnd - jsonStart);

            LlamaResponseQuery? structuredResponse;
            try
            {
                structuredResponse = JsonConvert.DeserializeObject<LlamaResponseQuery>(json);
                if (structuredResponse == null) return null;
                var dateValues = await GetDates(structuredResponse.Date);

                structuredResponse.Query = search;
                structuredResponse.DateRange = dateValues;
                if (structuredResponse.PodcastName != null)
                    structuredResponse.PodcastName = structuredResponse.PodcastName;
                if (structuredResponse.Keywords != null)
                    structuredResponse.Keywords = structuredResponse.Keywords;
                _logger.LogDebug(JsonConvert.SerializeObject(structuredResponse));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Cannot deserialize LLama response");
                return null;
            }

            return structuredResponse;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in Llama search");
            return null;
        }
    }
}