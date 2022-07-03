using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Proxy.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Proxy.Services;

public class UpdateTests
{
    [Test]
    public async Task Test()
    {
        var apiClient = new Mock<IMinecraftApiClient>();
        var producer = new Mock<INameProducer>();
        var result = new List<(string, string)>();
        apiClient.Setup(x => x.GetNameByUuid(It.IsAny<string>())).ReturnsAsync((string k) => new NameResponse()
        {
            Name = "test",
            Id = "test"
        });
        apiClient.Setup(x => x.LoadByNameBatch(It.IsAny<List<PlayerNameUpdate>>())).ReturnsAsync((List<PlayerNameUpdate> names) => new List<NameResponse>()
        {
            new NameResponse()
            {
                Name = "Technoblade",
                Id = "technoId"
            },
            new NameResponse()
            {
                Name = "Gronkh",
                Id = "gronkhId"
            }
        });
        producer.Setup(p => p.Produce(It.IsAny<string>(), It.IsAny<string>())).Callback((string a, string invoke) =>
        {
            result.Add((a, invoke));
        });
        var client = new BaseBackgroundService(null, null, NullLogger<BaseBackgroundService>.Instance, apiClient.Object, producer.Object);
        var nameList = new List<PlayerNameUpdate>();
        nameList.Add(new PlayerNameUpdate() { Uuid = "test", Name = "testNameChanged" });
        nameList.Add(new PlayerNameUpdate() { Uuid = "gronkhId", Name = "Gronkh" });
        nameList.Add(new PlayerNameUpdate() { Name = "technoblade" });
        await client.UpdateBatch(nameList);
        Assert.AreEqual(2, result.Count);
        // api is case insensitive
        Assert.AreEqual("Technoblade", result[0].Item1);
        Assert.AreEqual("technoId", result[0].Item2);
        Assert.AreEqual("test", result[1].Item1);
        Assert.AreEqual("test", result[1].Item2);
    }
}
