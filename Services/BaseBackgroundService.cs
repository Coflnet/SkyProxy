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
public class BaseBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    IMinecraftApiClient minecraftApiClient,
    INameProducer producer) : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory = scopeFactory;
    private readonly IConfiguration _config = config;
    private readonly IMinecraftApiClient minecraftApiClient = minecraftApiClient;
    private readonly INameProducer producer = producer;
    private Prometheus.Counter consumeCount = Prometheus.Metrics.CreateCounter("sky_base_consume", "How many messages were consumed");

    /// <summary>
    /// Called by asp.net on startup
    /// </summary>
    /// <param name="stoppingToken">is canceled when the applications stops</param>
    /// <returns></returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var toUpdateCons = Kafka.KafkaConsumer.ConsumeBatch<PlayerNameUpdate>(_config, _config["TOPICS:NAME_UPDATE_REQUEST"], async batch =>
        {
            await UpdateBatch(batch.ToList());
            consumeCount.Inc(batch.Count());
        }, stoppingToken, "sky-proxy", 10);
        var recheckRequest = Kafka.KafkaConsumer.ConsumeBatch<SaveAuction>(_config, _config["TOPICS:AUCTION_CHECK"], async batch =>
        {
            var differentSellers = batch.Where(a => IsNotBaiting(a)).Select(x => x.AuctioneerId).Distinct().ToList();
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BaseService>();
            foreach (var item in differentSellers)
            {
                await db.UpdateAh(item, "missing auction");
            }

            static bool IsNotBaiting(SaveAuction a)
            {
                return a.Start < DateTime.UtcNow.AddMinutes(2);
            }
        }, stoppingToken, "sky-proxy", 50, Confluent.Kafka.AutoOffsetReset.Latest);

        await toUpdateCons;
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
