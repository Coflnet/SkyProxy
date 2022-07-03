using System.Threading.Tasks;
using Coflnet.Sky.Proxy.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Confluent.Kafka.Admin;

namespace Coflnet.Sky.Proxy.Services;

public interface INameProducer
{
    void Dispose();
    Task Produce(string name, string uuid);
}

public class NameProducer : IDisposable, INameProducer
{
    private IConfiguration config;
    private ILogger<NameProducer> logger;
    IProducer<Ignore, PlayerNameUpdate> producer;
    public NameProducer(IConfiguration config, ILogger<NameProducer> logger)
    {
        this.config = config;
        this.logger = logger;
        var producerConfig = new ProducerConfig
        {

        };
        producer = new ProducerBuilder<Ignore, PlayerNameUpdate>(producerConfig).Build();

        Task.Run(async () =>
        {
            await UpdatePartitionCount(config, logger);
        });
    }

    private static async Task UpdatePartitionCount(IConfiguration config, ILogger<NameProducer> logger)
    {
        try
        {
            using var adminClient = new AdminClientBuilder(new AdminClientConfig()
            {
                BootstrapServers = config["KAFKA_HOST"]
            }).Build();
            await adminClient.CreatePartitionsAsync(new List<PartitionsSpecification>()
                {
                    new PartitionsSpecification()
                    {
                        Topic = config["TOPICS:NAME_UPDATE_REQUEST"],
                        IncreaseTo = 30
                    }
                });
        }
        catch (System.Exception e)
        {
            logger.LogError(e, "Error updating topic");
        }
    }

    public void Dispose()
    {
        producer?.Dispose();
        producer = null;
    }

    public async Task Produce(string name, string uuid)
    {

        await producer.ProduceAsync(config["TOPICS:NEW_NAME"], new Message<Ignore, PlayerNameUpdate>()
        {
            Value = new PlayerNameUpdate()
            {
                Name = name,
                Uuid = uuid
            }
        });
    }
}
