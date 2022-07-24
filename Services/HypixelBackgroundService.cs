using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using System.Linq;
using Coflnet.Sky.Core;
using System;
using Microsoft.Extensions.Logging;
using Prometheus;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.Proxy.Services;

public class HypixelBackgroundService : BackgroundService
{
    private Prometheus.Counter consumeCount = Prometheus.Metrics.CreateCounter("sky_proxy_user_ah_update", "How many messages were consumed");
    private IConfiguration config;
    private ILogger<HypixelBackgroundService> logger;
    private IServiceScopeFactory scopeFactory;
    public HypixelBackgroundService(IConfiguration config, ILogger<HypixelBackgroundService> logger, IServiceScopeFactory scopeFactory)
    {
        this.config = config;
        this.logger = logger;
        this.scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ProducerConfig producerConfig = new ProducerConfig
        {
            BootstrapServers = SimplerConfig.Config.Instance["KAFKA_HOST"],
            LingerMs = 100,
        };
        using var p = new ProducerBuilder<string, SaveAuction>(producerConfig).SetValueSerializer(SerializerFactory.GetSerializer<SaveAuction>()).Build();

        string key = null;
        using (var scope = scopeFactory.CreateScope())
        {
            var keyRetriever = scope.ServiceProvider.GetRequiredService<KeyManager>();
            key = await keyRetriever.GetKey("hypixel");
        }
        var lastUseSet = new DateTime();

        await Coflnet.Kafka.KafkaConsumer.ConsumeBatch<string>(config["KAFKA_HOST"], config["TOPICS:USER_AH_UPDATE"], async batch =>
        {
            foreach (var lp in batch)
            {
                await Sky.Updater.MissingChecker.UpdatePlayerAuctions(lp, p, key);
            }
            consumeCount.Inc(batch.Count());

            if(lastUseSet < DateTime.Now-TimeSpan.FromMinutes(1))
            {
                // minimize db writes by not writing use every time
                lastUseSet = DateTime.Now;
                using (var scope = scopeFactory.CreateScope())
                {
                    var keyRetriever = scope.ServiceProvider.GetRequiredService<KeyManager>();
                    await keyRetriever.UsedKey("hypixel", key);
                }
            }
        }, stoppingToken, "SkyProxy", 5);
        p.Flush(TimeSpan.FromSeconds(10));
    }
}
