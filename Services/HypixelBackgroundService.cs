using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using System.Linq;
using Coflnet.Sky.Core;
using System;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Updater;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Microsoft.EntityFrameworkCore;
using RestSharp;

namespace Coflnet.Sky.Proxy.Services;

public class HypixelBackgroundService : BackgroundService
{
    private Prometheus.Counter consumeCount = Prometheus.Metrics.CreateCounter("sky_proxy_user_ah_update", "How many messages were consumed");
    private IConfiguration config;
    private ILogger<HypixelBackgroundService> logger;
    private IServiceScopeFactory scopeFactory;
    private ConnectionMultiplexer redis;
    private MissingChecker missingChecker;
    public HypixelBackgroundService(IConfiguration config,
                                    ILogger<HypixelBackgroundService> logger,
                                    IServiceScopeFactory scopeFactory,
                                    ConnectionMultiplexer redis,
                                    MissingChecker missingChecker)
    {
        this.config = config;
        this.logger = logger;
        this.scopeFactory = scopeFactory;
        this.redis = redis;
        this.missingChecker = missingChecker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ProducerConfig producerConfig = new ProducerConfig
        {
            BootstrapServers = SimplerConfig.Config.Instance["KAFKA_HOST"],
            LingerMs = 100,
        };
        using var p = new ProducerBuilder<string, SaveAuction>(producerConfig).SetValueSerializer(SerializerFactory.GetSerializer<SaveAuction>()).Build();


        var lastUseSet = new DateTime();
        var db = redis.GetDatabase();
        try
        {
            await db.StreamCreateConsumerGroupAsync("ah-update", "sky-proxy-ah-update");
        }
        catch (System.Exception)
        {
            // ignore
        }


        using (var scope = scopeFactory.CreateScope())
        using (var context = scope.ServiceProvider.GetRequiredService<Models.ProxyDbContext>())
        {
            // make sure all migrations are applied
            await context.Database.MigrateAsync();
        }
        string key = null;
        key = await GetValidKey(key);
        logger.LogInformation("retrieved key, start processing");

        var pollNoContentTimes = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var elements = await db.StreamReadGroupAsync("ah-update", "sky-proxy-ah-update", System.Net.Dns.GetHostName(), StreamPosition.NewMessages, 3);
            if (elements.Count() == 0)
            {
                await Task.Delay(500 * ++pollNoContentTimes);
                continue;
            }
            pollNoContentTimes = 0;
            foreach (var item in elements)
            {
                var playerId = item["uuid"];
                Console.WriteLine($"got PlayerId: {playerId}");
                try
                {
                    await missingChecker.UpdatePlayerAuctions(playerId, p, key);
                    consumeCount.Inc();
                }
                catch (Exception e)
                {
                    logger.LogError(e, "error updating auctions");
                    int attempt = ((int)item["try"]);
                    if (attempt < 3)
                        await db.StreamAddAsync("ah-update", new NameValueEntry[] { new NameValueEntry("uuid", playerId), new NameValueEntry("try", attempt + 1) });
                }
                await db.StreamAcknowledgeAsync("ah-update", "sky-proxy-ah-update", item.Id, CommandFlags.FireAndForget);
            }
            await UsedKey(key, lastUseSet, elements.Count());
        }
    }

    private async Task<string> GetValidKey(string key)
    {
        using var scope = scopeFactory.CreateScope();
        var keyRetriever = scope.ServiceProvider.GetRequiredService<KeyManager>();
        while (key == null)
            try
            {
                key = await keyRetriever.GetKey("hypixel");
                var client = new RestClient("https://api.hypixel.net/");
                var request = new RestRequest($"key?key={key}", Method.Get);

                //Get the response and Deserialize
                var response = client.Execute(request);
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    logger.LogInformation($"key `{key.Truncate(10)}`is invalid");
                    await keyRetriever.InvalidateKey("hypixel", key);
                    key = null;
                    await Task.Delay(Random.Shared.Next(1000, 500000));
                }
                else
                    await keyRetriever.UsedKey("hypixel", key);
            }
            catch (CoflnetException e)
            {
                logger.LogInformation(e.Message);
                await Task.Delay(15000);
            }

        return key;
    }

    private async Task<DateTime> UsedKey(string key, DateTime lastUseSet, int times = 1)
    {
        if (lastUseSet < DateTime.UtcNow - TimeSpan.FromMinutes(1))
        {
            // minimize db writes by not writing use every time
            lastUseSet = DateTime.UtcNow;
            using (var scope = scopeFactory.CreateScope())
            {
                var keyRetriever = scope.ServiceProvider.GetRequiredService<KeyManager>();
                await keyRetriever.UsedKey("hypixel", key, times);
            }
        }

        return lastUseSet;
    }
}
