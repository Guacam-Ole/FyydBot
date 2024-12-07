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
        var services = AddServices(JsonConvert.DeserializeObject<Secrets>(File.ReadAllText("secrets.json")));
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
                    if (notification?.Status?.Content == null) continue;
                    var searchContents = await llama.ParseSearchQuery(mastodon.StripHtml(notification.Status.Content));
                    if (searchContents == null)
                    {
                        logger!.LogWarning("Failed to parse search query: '{content}'",notification.Status.Content);
                        await mastodon.SendErrorResponse(notification.Status, notification.Status.Visibility, "Sorry. Ich versteh das nicht wirklich. Bin ja auch nur ein dummer Bot. Wenn Du mir gar keine Frage stellen wolltest. Ignoriere das einfach :)");
                        await mastodon.DismissNotification(notification.Id);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(searchContents.PodcastName) && string.IsNullOrWhiteSpace(searchContents.Keywords))
                    {
                        logger!.LogWarning("Succesfully parsde search query, but no keywords found: '{content}'",notification.Status.Content);
                        await mastodon.SendErrorResponse(notification.Status, notification.Status.Visibility, "Ich konnte weder einen Podcast, noch einen Suchbegriff aus Deiner Anfrage bestimmen.\nBeispiel für eine Anfrage, die ich verstehe:'Ich suche einen Podcast über Gummibärchen'");
                        continue;
                    }

                    var searchResponse = await fyyd.SearchForEpisode(searchContents);
                    if (searchResponse == null || searchResponse.Count == 0)
                    {
                        logger!.LogInformation("Succesfully parsed search query, but no matchs from Fyyd: '{content}'",notification.Status.Content);
                        await mastodon.SendErrorResponse(notification.Status, notification.Status.Visibility, "Ich habe Dich zwar verstanden, aber keine passenden Podcasts gefunden.");
                        continue;
                    }
                    await mastodon.CreateResponseFromSearchResult(notification.Status, searchContents, notification.Status.Visibility, searchResponse);
                    await mastodon.DismissNotification(notification.Id);
                }

                Thread.Sleep(TimeSpan.FromSeconds(10));
            }
            catch (Exception e)
            {
                logger.LogError(e, "error in loop");
                throw;
            }
        }
    }

    private static ServiceProvider AddServices(Secrets secrets)
    {
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
        services.AddScoped<Mastodon>();
        services.AddScoped<Llama>();
        services.AddScoped<Fyyd>();


        var provider = services.BuildServiceProvider();
        return provider;
    }
}