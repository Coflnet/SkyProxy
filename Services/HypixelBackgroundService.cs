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
    private Coflnet.Sky.Kafka.KafkaCreator kafkaCreator;
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
                                    Coflnet.Sky.Kafka.KafkaCreator kafkaCreator,
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
        public int Try { get; set; } = 0;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AuctionProducer = kafkaCreator.BuildProducer<string, SaveAuction>();

        var lastUseSet = new DateTime();
        var db = redis.GetDatabase();

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
                if (e.Message.Contains("OGROUP No such key"))
                {
                    // group doesn't exist, create it
                    await db.StreamCreateConsumerGroupAsync("ah-update", "sky-proxy-ah-update");
                }
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
            var hints = elements.Select(e =>
            {
                var json = e["uuid"].ToString();
                if (!json.StartsWith("{"))
                {
                    logger.LogError("invalid json: " + json);
                    return null;
                }
                var hint = JsonConvert.DeserializeObject<Hint>(json);
                return hint;
            }).Where(h => h != null).GroupBy(x => x.Uuid).Select(x => x.OrderBy(y => y.ProvidedAt).First()).ToArray();

            // acknowledge all messages gonna get resheduled if they fail
            foreach (var item in elements)
                await db.StreamAcknowledgeAsync("ah-update", "sky-proxy-ah-update", item.Id, CommandFlags.FireAndForget);
            if (hints.Count(h => h.ProvidedAt < DateTime.UtcNow - TimeSpan.FromSeconds(9)) > 2)
            {
                logger.LogInformation($"skipping {hints.Count(h => h.ProvidedAt < DateTime.UtcNow - TimeSpan.FromSeconds(9))} old hints");
                continue;
            }
            Task batch = ExecuteBatch(db, key, hints, activity, stoppingToken);
            await UsedKey(key, lastUseSet, elements.Count());
            // wait for batch or timeout after 400ms
            await Task.WhenAny(batch, Task.Delay(400));
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

    private Task ExecuteBatch(IDatabase db, string key, Hint[] elements, Activity activity, CancellationToken stoppingToken)
    {
        var timeoutToken = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token).Token;
        var skipCount = 0;
        var cancelOnSkipToken = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        return Task.WhenAny(Task.Delay(30000, cancelOnSkipToken.Token),
         Parallel.ForEachAsync(elements, timeoutToken, async (hint, cancel) =>
        {
            using var requestActivity = activitySource.StartActivity("ah-update-request")?.SetParentId(activity.Id);

            consumeCount.Inc();
            if (hint.ProvidedAt < DateTime.UtcNow - TimeSpan.FromSeconds(6) && hint.hintSource.Contains("recheck"))
            {
                logger.LogInformation($"skipping recheck because it is to old {hint.Uuid}");
                requestActivity?.SetTag("skiped", "true");
                skipCount++;
                return;
            }
            if (hint.ProvidedAt < DateTime.UtcNow - TimeSpan.FromSeconds(9))
            {
                logger.LogInformation($"skipping hint because it is to old {hint.Uuid} from {hint.hintSource}");
                requestActivity?.SetTag("skiped", "true");
                skipCount++;
                if (skipCount >= elements.Length - 1)
                    cancelOnSkipToken.Cancel();
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
                    logger.LogError($"invalid player for {playerId}");
                    return;
                }
                requestCount.Inc();
                Console.WriteLine($"checking {playerId} because of {hint.hintSource}");
                await missingChecker.UpdatePlayerAuctions(playerId, AuctionProducer, key, new("pre-api", hint.hintSource));
                successCount.Inc();
            }
            catch (Exception e)
            {
                hadError = true;
                logger.LogError(e, "error updating auctions");
                if (e.Message.Contains("Invalid API key"))
                {
                    logger.LogInformation($"key `{key.Truncate(10)}` is invalid");
                    using var scope = scopeFactory.CreateScope();
                    var keyRetriever = scope.ServiceProvider.GetRequiredService<KeyManager>();
                    await keyRetriever.InvalidateKey("hypixel", key);
                }
                int attempt = hint.Try++;
                requestActivity?.SetTag("error", "true");
                if (attempt < 3 && !cancel.IsCancellationRequested)
                    await db.StreamAddAsync("ah-update", new NameValueEntry[] { new NameValueEntry("uuid", JsonConvert.SerializeObject(hint)) });
            }
            var waitTime = TimeSpan.FromSeconds(40) - (DateTime.Now - start);
            if (waitTime > TimeSpan.Zero && !cancel.IsCancellationRequested)
            {
                await Task.Delay(waitTime);
                requestActivity?.SetTag("waited", waitTime.ToString());
            }
        }));
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
        var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
        var lease = await rateLimiter.AcquireAsync(1, timeoutToken).ConfigureAwait(false);
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
