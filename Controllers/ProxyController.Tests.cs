using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using NUnit.Framework;
using Swashbuckle.AspNetCore.Swagger;

namespace Coflnet.Sky.Proxy.Controllers;

public class ProxyControllerSchemaTests
{
    /// <summary>
    /// The generated client derives its return type from the declared 200 response. Without a declared
    /// string body the client method returns nothing and consumers can no longer read the raw hypixel json.
    /// </summary>
    [Test]
    public void HypixelProxyDeclaresStringResponse()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IWebHostEnvironment>(new TestEnvironment());
        services.AddSingleton<IHostEnvironment>(sp => sp.GetRequiredService<IWebHostEnvironment>());
        new Startup(configuration).ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var document = provider.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        var response = document.Paths["/Proxy/hypixel"].Operations[System.Net.Http.HttpMethod.Get].Responses["200"];
        Assert.That(response.Content.Keys, Does.Contain("application/json"));
        Assert.That(response.Content["application/json"].Schema.Type, Is.EqualTo(JsonSchemaType.String));
    }

    private class TestEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ApplicationName { get; set; } = "SkyProxy";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Development";
    }
}
