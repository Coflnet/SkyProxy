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
using RedisRateLimiting;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using System.Diagnostics;

namespace Coflnet.Sky.Proxy.Services;

public class HypixelBackgroundService : BackgroundService
{
    private Prometheus.Counter consumeCount = Prometheus.Metrics.CreateCounter("sky_proxy_ah_update_request", "How many messages were consumed");
    private Prometheus.Counter requestCount = Prometheus.Metrics.CreateCounter("sky_proxy_ah_update_start", "How many requests were started");
    private Prometheus.Counter successCount = Prometheus.Metrics.CreateCounter("sky_proxy_ah_update_success", "How many time successful update happened");
    private IConfiguration config;
    private ILogger<HypixelBackgroundService> logger;
    private IServiceScopeFactory scopeFactory;
    private ConnectionMultiplexer redis;
    private MissingChecker missingChecker;
    private Kafka.KafkaCreator kafkaCreator;
    private ActivitySource activitySource;
    /// <summary>
    /// Auction producer for kafka
    /// </summary>
    public IProducer<string, SaveAuction> AuctionProducer;
    private ConcurrentDictionary<string, RedisConcurrencyRateLimiter<string>> rateLimiters = new();
    public HypixelBackgroundService(IConfiguration config,
                                    ILogger<HypixelBackgroundService> logger,
                                    IServiceScopeFactory scopeFactory,
                                    ConnectionMultiplexer redis,
                                    MissingChecker missingChecker,
                                    Kafka.KafkaCreator kafkaCreator,
                                    ActivitySource activitySource)
    {
        this.config = config;
        this.logger = logger;
        this.scopeFactory = scopeFactory;
        this.redis = redis;
        this.missingChecker = missingChecker;
        this.kafkaCreator = kafkaCreator;
        this.activitySource = activitySource;
    }

    public class Hint
    {
        public string Uuid { get; set; }
        public string hintSource { get; set; }
        public DateTime ProvidedAt { get; set; } = DateTime.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AuctionProducer = kafkaCreator.BuildProducer<string, SaveAuction>();

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

        _ = Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000 * 60 * 60);
                try
                {
                    key = await GetValidKey(key);
                }
                catch (System.Exception e)
                {
                    logger.LogError(e, "error getting key");
                }
            }
        }, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecutePull(lastUseSet, db, key, stoppingToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "error executing pull");
            }
        }
    }
    private bool hadError = false;

    private async Task ExecutePull(DateTime lastUseSet, IDatabase db, string key, CancellationToken stoppingToken)
    {
        var pollNoContentTimes = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            using var activity = activitySource.StartActivity("ah-update-batch");
            StreamEntry[] elements;
            try
            {
                elements = await db.StreamReadGroupAsync("ah-update", "sky-proxy-ah-update", System.Net.Dns.GetHostName(), StreamPosition.NewMessages, 4);
            }
            catch (RedisTimeoutException)
            {
                logger.LogInformation("redis timeout while reading stream. Trying again");
                continue;
            }
            if (elements.Count() == 0)
            {
                await Task.Delay(500 * ++pollNoContentTimes);
                continue;
            }
            pollNoContentTimes = 0;
            // deduplicate 
            elements = elements.GroupBy(x => x["uuid"]).Select(x => x.First()).ToArray();
            Task batch = ExecuteBatch(db, key, elements, activity, stoppingToken);
            await UsedKey(key, lastUseSet, elements.Count());
            await Task.Delay(500);
            if (hadError)
            {
                logger.LogInformation("had error, waiting 10 seconds");
                await Task.Delay(TimeSpan.FromSeconds(5 + Random.Shared.NextDouble() * 10)); // back off in favor of another instance
                hadError = false;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await batch;
                }
                catch (System.Exception e)
                {
                    logger.LogError(e, "error executing batch");
                }
            });
        }
    }

    private Task ExecuteBatch(IDatabase db, string key, StreamEntry[] elements, Activity activity, CancellationToken stoppingToken)
    {
        var cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token).Token;
        return Parallel.ForEachAsync(elements, cancellationToken, async (item, cancel) =>
        {
            using var requestActivity = activitySource.StartActivity("ah-update-request")?.SetParentId(activity.Id);
            var json = item["uuid"].ToString();
            if (!json.StartsWith("{"))
            {
                logger.LogError("invalid json: " + json);
                return;
            }
            var hint = JsonConvert.DeserializeObject<Hint>(json);
            consumeCount.Inc();
            if (hint.ProvidedAt < DateTime.UtcNow - TimeSpan.FromSeconds(6) && hint.hintSource == "recheck")
            {
                logger.LogInformation($"skipping recheck because it is to old {hint.Uuid}");
                requestActivity?.SetTag("skiped", "true");
                return;
            }
            if (hint.ProvidedAt < DateTime.UtcNow - TimeSpan.FromSeconds(9))
            {
                logger.LogInformation($"skipping hint because it is to old {hint.Uuid} from {hint.hintSource}");
                requestActivity?.SetTag("skiped", "true");
                return;
            }
            var playerId = hint.Uuid;
            var start = DateTime.Now;
            using var lease = await GetLeaseFor(playerId);
            if (!lease.IsAcquired)
            {
                Console.WriteLine("rate limit reached, skip " + playerId);
                requestActivity?.SetTag("skiped", "rate limit");
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(playerId) || playerId.Length != 32)
                {
                    logger.LogError($"invalid player for {json}");
                    return;
                }
                requestCount.Inc();
                await missingChecker.UpdatePlayerAuctions(playerId, AuctionProducer, key, new("pre-api", hint.hintSource));
                successCount.Inc();
            }
            catch (Exception e)
            {
                hadError = true;
                logger.LogError(e, "error updating auctions");
                int attempt = ((int)item["try"]);
                requestActivity?.SetTag("error", "true");
                if (attempt < 3 && !cancel.IsCancellationRequested)
                    await db.StreamAddAsync("ah-update", new NameValueEntry[] { new NameValueEntry("uuid", json), new NameValueEntry("try", attempt + 1) });
            }
            await db.StreamAcknowledgeAsync("ah-update", "sky-proxy-ah-update", item.Id, CommandFlags.FireAndForget);
            var waitTime = TimeSpan.FromSeconds(18) - (DateTime.Now - start);
            if (waitTime > TimeSpan.Zero && !cancel.IsCancellationRequested)
            {
                await Task.Delay(waitTime);
                requestActivity?.SetTag("waited", waitTime.ToString());
            }
        });
    }

    public async Task<RateLimitLease> GetLeaseFor(string playerId)
    {
        var rateLimiter = rateLimiters.GetOrAdd(playerId, (m) => new RedisConcurrencyRateLimiter<string>(m, new()
        {
            QueueLimit = 1,
            ConnectionMultiplexerFactory = () => redis,
            PermitLimit = 1,
            TryDequeuePeriod = TimeSpan.FromSeconds(1.0)
        }));
        var lease = await rateLimiter.AcquireAsync().ConfigureAwait(false);
        return lease;
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
                var request = new RestRequest($"punishmentstats", Method.Get);
                request.AddHeader("API-Key", key);

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
