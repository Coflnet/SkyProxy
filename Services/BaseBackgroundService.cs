using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Proxy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using System.Linq;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Proxy.Controllers;
using Coflnet.Sky.Core;
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
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        // make sure all migrations are applied
        await context.Database.MigrateAsync();

        var flipCons = Coflnet.Kafka.KafkaConsumer.ConsumeBatch<LowPricedAuction>(config["KAFKA_HOST"], config["TOPICS:LOW_PRICED"], async batch =>
        {
            var service = GetService();
            foreach (var lp in batch)
            {
                // do something
            }
            consumeCount.Inc(batch.Count());
        }, stoppingToken, "SkyProxy");

        var toUpdateCons = Coflnet.Kafka.KafkaConsumer.ConsumeBatch<PlayerNameUpdate>(config["KAFKA_HOST"], config["TOPICS:NAME_UPDATE_REQUEST"], async batch =>
        {
            var service = GetService();
            foreach (var lp in batch)
            {
                // do something
            }
            consumeCount.Inc(batch.Count());
        }, stoppingToken, "SkyProxy", 10);
        var sync = Task.Run(async () =>
        {
            var totalReq = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000);
                try
                {
                    var nameList = new List<PlayerNameUpdate>(){
                        new PlayerNameUpdate(){
                            Uuid = "a2080281c2784181b961d99ed2f3347c",
                            Name = "Gronkh"
                        },
                        new PlayerNameUpdate(){
                            Uuid = "f7c77d999f154a66a87dc4a51ef30d19",
                            Name = "hypixel"
                        },
                        new PlayerNameUpdate(){
                            Uuid = "1c15140626cb40a6821cef27731d6182",
                            Name = "jake"
                        },
                        new PlayerNameUpdate(){
                            Uuid = "853c80ef3c3749fdaa49938b674adae6",
                            Name = "jeb_"
                        },
                        new PlayerNameUpdate(){
                            Uuid = "0fa0416373d3462ea55d70e4887291aa",
                            Name = "SparkofPhoenix"
                        },
                        new PlayerNameUpdate(){
                            Uuid = "924c4401e3cd4baaadf186be82aed375",
                            Name = "stepsisters"
                        },
                        new PlayerNameUpdate(){
                            Uuid = "b876ec32e396476ba1158438d83c67d4",
                            Name = "Technoblade"
                        }
                    };
                    //var nameList = new string[] { "Technoblade", "hypixel", "Sparkofphoenix", "gronkh", "jeb_", "stepsisters", "jake", "jakobspielt", "lukasimo14" };
                    await UpdateBatch(nameList);
                }
                catch (System.Exception e)
                {
                    logger.LogError(e, "Error in sync");
                }
            }
        });
        await sync;
        await Task.WhenAll(flipCons, sync, toUpdateCons);
    }

    public async Task UpdateBatch(List<PlayerNameUpdate> nameList)
    {
        if(nameList.Count < 10)
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
            if(user != null)
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
