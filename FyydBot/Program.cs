using Mastonet;
using Mastonet.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NReco.Logging.File;

namespace FyydBot;

public class FyydBot
{
    public static async Task Main(string[] args)
    {
        var services = AddServices();
        var mastodon = services.GetService<Mastodon>();
        var llama = services.GetService<Llama>() ?? throw new NullReferenceException();
        var fyyd = services.GetService<Fyyd>() ?? throw new NullReferenceException();
        var logger = services.GetService<ILogger<FyydBot>>()?? throw new NullReferenceException();

        logger.LogInformation("FyddBot started");

        while (true)
        {
            try
            {
                List<Notification> notifications;
                try
                {
                    notifications = await mastodon!.GetUnreadNotifications();
                    if ( notifications.Count != 0) logger.LogInformation("Found '{count}' unread notifications", notifications.Count);
                }
                catch (Exception e)
                {
                    logger!.LogError(e, "Failed reading from Mastodon. Will wait for a minute before trying again");
                    Thread.Sleep(TimeSpan.FromSeconds(60));
                    continue;
                }

                foreach (var notification in notifications)
                {
                    await mastodon.DismissNotification(notification.Id);
                    if (notification?.Status?.Content == null) continue;
                    if (!string.IsNullOrEmpty(notification?.Status?.InReplyToId)) continue; // no discussion thread
                    var replyId=await mastodon.ReplyTo(notification.Status,
                        "Alles klar, ich schau mal ob ich da was finde \ud83d\udd0e (kann ein paar Minuten dauern \u231a\ufe0f )");
                    
                    var searchContents = await llama.ParseSearchQuery(notification.Status.Content.StripHtml());
                    if (searchContents == null)
                    {
                        logger!.LogWarning("Failed to parse search query: '{content}'",notification.Status.Content);
                        await mastodon.SendErrorResponse(notification.Status, replyId, "Sorry. Ich versteh das nicht wirklich. Bin ja auch nur ein dummer Bot. \ud83e\udd16 Wenn Du mir gar keine Frage stellen wolltest. Ignoriere das einfach :)");
                        await mastodon.DismissNotification(notification.Id);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(searchContents.PodcastName) && string.IsNullOrWhiteSpace(searchContents.Keywords))
                    {
                        logger!.LogWarning("Succesfully parsed search query, but no keywords found: '{content}'",notification.Status.Content);
                        await mastodon.SendErrorResponse(notification.Status, replyId, "Ich konnte weder einen Podcast, noch einen Suchbegriff aus Deiner Anfrage bestimmen.\u2753\ufe0f\nBeispiel für eine Anfrage, die ich verstehe:'Ich suche einen Podcast über Gummibärchen'");
                        continue;
                    }

                    await mastodon.UpdateSingleMessage(notification.Status.Account, replyId,
                        $"Ich sammel mal eben die Podcasts zu '{string.Join(' ',searchContents.Keywords)}' zusammen. Einen kleinen Moment noch");
                    
                    var searchResponse = await fyyd.SearchForEpisode(searchContents);
                    if (searchResponse == null || searchResponse.Count == 0)
                    {
                        logger!.LogInformation("Succesfully parsed search query, but no matchs from Fyyd: '{content}'",notification.Status.Content);
                        await mastodon.SendErrorResponse(notification.Status, replyId, "Ich habe Dich zwar verstanden, aber keine passenden Podcasts gefunden. \ud83e\udd37");
                        continue;
                    }
                    await mastodon.CreateResponseFromSearchResult(notification.Status, replyId, searchContents, searchResponse);
                   
                }

                Thread.Sleep(TimeSpan.FromSeconds(10));
            }
            catch (Exception e)
            {
                logger.LogError(e, "error in loop");
                Console.WriteLine(e);
                return; 
            }
        }
    }

    private static ServiceProvider AddServices()
    {
        var secrets = JsonConvert.DeserializeObject<Secrets>(File.ReadAllText("secrets.json"))?? throw new NullReferenceException();
        var config= JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json")) ?? throw new NullReferenceException();
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddSimpleConsole(options => { options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Disabled; });
            var logFile = "fyydbot.log";
            logging.AddFile(logFile, conf =>
            {
                conf.MinLevel = LogLevel.Debug;
                conf.Append = true;
                conf.MaxRollingFiles = 1;
                conf.FileSizeLimitBytes = 100000;
            });
        });
        services.AddSingleton(secrets);
        services.AddSingleton(config);
        services.AddScoped<Mastodon>();
        services.AddScoped<Llama>();
        services.AddScoped<Fyyd>();


        var provider = services.BuildServiceProvider();
        return provider;
    }
}