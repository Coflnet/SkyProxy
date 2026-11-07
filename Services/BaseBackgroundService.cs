using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Proxy.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Proxy.Services;

/// <summary>
/// Background worker that consumes name-update and auction-check batches.
/// </summary>
/// <param name="scopeFactory">Factory used to create DI scopes for scoped services.</param>
/// <param name="config">Application configuration.</param>
/// <param name="minecraftApiClient">API client used to resolve player names and uuids.</param>
/// <param name="producer">Producer used to publish name updates.</param>
/// <param name="logger">Logger for consumer failures.</param>
public class BaseBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    IMinecraftApiClient minecraftApiClient,
    INameProducer producer,
    ILogger<BaseBackgroundService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory = scopeFactory;
    private readonly IConfiguration _config = config;
    private readonly IMinecraftApiClient minecraftApiClient = minecraftApiClient;
    private readonly INameProducer producer = producer;
    private readonly ILogger<BaseBackgroundService> logger = logger;
    private Prometheus.Counter consumeCount = Prometheus.Metrics.CreateCounter("sky_base_consume", "How many messages were consumed");

    /// <summary>
    /// Called by asp.net on startup
    /// </summary>
    /// <param name="stoppingToken">is canceled when the applications stops</param>
    /// <returns></returns>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // each consumer retries on its own; both only end when the service is stopped
        return Task.WhenAll(
            KeepConsuming("name-update", backoff => ConsumeNameUpdates(backoff, stoppingToken), stoppingToken),
            KeepConsuming("auction-check", backoff => ConsumeAuctionChecks(backoff, stoppingToken), stoppingToken));
    }

    private Task KeepConsuming(string name, Func<ConsumeBackoff, Task> consumeOnce, CancellationToken stoppingToken)
    {
        var backoff = new ConsumeBackoff();
        return ConsumeRetryLoop.RunAsync(name, () => consumeOnce(backoff), backoff, logger, stoppingToken);
    }

    private Task ConsumeNameUpdates(ConsumeBackoff backoff, CancellationToken stoppingToken)
    {
        return Kafka.KafkaConsumer.ConsumeBatch<PlayerNameUpdate>(_config, _config["TOPICS:NAME_UPDATE_REQUEST"], async batch =>
        {
            await UpdateBatch(batch.ToList());
            consumeCount.Inc(batch.Count());
            backoff.Reset();
        }, stoppingToken, "sky-proxy", 10);
    }

    private Task ConsumeAuctionChecks(ConsumeBackoff backoff, CancellationToken stoppingToken)
    {
        return Kafka.KafkaConsumer.ConsumeBatch<SaveAuction>(_config, _config["TOPICS:AUCTION_CHECK"], async batch =>
        {
            await RecheckSellers(batch);
            backoff.Reset();
        }, stoppingToken, "sky-proxy", 50, Confluent.Kafka.AutoOffsetReset.Latest);
    }

    private async Task RecheckSellers(IEnumerable<SaveAuction> batch)
    {
        var differentSellers = batch.Where(a => IsNotBaiting(a)).Select(x => x.AuctioneerId).Distinct().ToList();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BaseService>();
        foreach (var item in differentSellers)
        {
            await db.UpdateAh(item, "missing auction");
        }
    }

    private static bool IsNotBaiting(SaveAuction a)
    {
        return a.Start < DateTime.UtcNow.AddMinutes(2);
    }

    public async Task UpdateBatch(List<PlayerNameUpdate> nameList)
    {
        if (nameList.Count < 10)
        {
            // Todo request more names to update from indexer
        }
        var responseJson = await minecraftApiClient.LoadByNameBatch(nameList);
        var lookup = responseJson.ToDictionary(x => x.Name.ToLower());
        var missing = nameList.Where(n => n.Name != null && !lookup.ContainsKey(n.Name.ToLower()));
        var updated = responseJson.Where(n => !nameList.Where(x => x.Uuid == n.Id).Any());
        foreach (var item in updated)
        {
            await producer.Produce(item.Name, item.Id);
        }
        foreach (var item in missing)
        {
            var user = await minecraftApiClient.GetNameByUuid(item.Uuid);
            if (user != null)
            {
                await producer.Produce(user.Name, item.Uuid);
            }
        }
    }
}
