using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Proxy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.Linq;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Proxy.Controllers;
using System.Collections.Generic;
using Confluent.Kafka.Admin;
using System;

namespace Coflnet.Sky.Proxy.Services;

public class BaseBackgroundService : BackgroundService
{
    private IServiceScopeFactory scopeFactory;
    private IConfiguration config;
    private ILogger<BaseBackgroundService> logger;
    private IMinecraftApiClient minecraftApiClient;
    private INameProducer producer;
    private Prometheus.Counter consumeCount = Prometheus.Metrics.CreateCounter("sky_base_conume", "How many messages were consumed");

    public BaseBackgroundService(
        IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<BaseBackgroundService> logger, IMinecraftApiClient minecraftApiClient, INameProducer producer)
    {
        this.scopeFactory = scopeFactory;
        this.config = config;
        this.logger = logger;
        this.minecraftApiClient = minecraftApiClient;
        this.producer = producer;
    }
    /// <summary>
    /// Called by asp.net on startup
    /// </summary>
    /// <param name="stoppingToken">is canceled when the applications stops</param>
    /// <returns></returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        var toUpdateCons = Coflnet.Kafka.KafkaConsumer.ConsumeBatch<PlayerNameUpdate>(config["KAFKA_HOST"], config["TOPICS:NAME_UPDATE_REQUEST"], async batch =>
        {
            await UpdateBatch(batch.ToList());
            consumeCount.Inc(batch.Count());
        }, stoppingToken, "SkyProxy", 10);

        await toUpdateCons;
    }

    public async Task UpdateBatch(List<PlayerNameUpdate> nameList)
    {
        if (nameList.Count < 10)
        {
            // Todo request more names to update from indexer
        }
        var responseJson = await this.minecraftApiClient.LoadByNameBatch(nameList);
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

    private BaseService GetService()
    {
        return scopeFactory.CreateScope().ServiceProvider.GetRequiredService<BaseService>();
    }
}
