using System.Threading.Tasks;
using Coflnet.Sky.Proxy.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Confluent.Kafka.Admin;
using Coflnet.Sky.Core;
using Coflnet.Kafka;

namespace Coflnet.Sky.Proxy.Services;


public interface INameProducer : IDisposable
{
    void Dispose();
    Task Produce(string name, string uuid);
}

public class NameProducer : INameProducer
{
    private IConfiguration config;
    private ILogger<NameProducer> logger;
    IProducer<Ignore, PlayerNameUpdate> producer;
    public NameProducer(IConfiguration config, ILogger<NameProducer> logger, Kafka.KafkaCreator kafkaCreator)
    {
        this.config = config;
        this.logger = logger;
        producer = kafkaCreator.BuildProducer<Ignore, PlayerNameUpdate>();

        Task.Run(async () =>
        {
            await UpdatePartitionCount(config, logger, kafkaCreator);
        });
    }

    private static async Task UpdatePartitionCount(IConfiguration config, ILogger<NameProducer> logger, Kafka.KafkaCreator kafkaCreator)
    {
        try
        {
            await kafkaCreator.CreateTopicIfNotExist(config["TOPICS:NAME_UPDATE_REQUEST"], 30);
        }
        catch (System.Exception e)
        {
            logger.LogError(e, "Error updating topic");
        }
    }

    /// <summary>
    /// Disposes the producer
    /// </summary>
    public void Dispose()
    {
        producer?.Dispose();
        producer = null;
    }

    /// <summary>
    /// Produces a new name update request
    /// </summary>
    /// <param name="name"></param>
    /// <param name="uuid"></param>
    /// <returns></returns>
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
