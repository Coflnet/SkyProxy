using System;
using System.IO;
using System.Reflection;
using Coflnet.Sky.Proxy.Models;
using Coflnet.Sky.Proxy.Services;
using Coflnet.Sky.Core;
using Coflnet.Sky.Updater;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Prometheus;
using StackExchange.Redis;

namespace Coflnet.Sky.Proxy
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "SkyProxy", Version = "v1" });
                // Set the comments path for the Swagger JSON and UI.
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
            });

            // Replace 'YourDbContext' with the name of your own DbContext derived class.
            services.AddDbContext<ProxyDbContext>(
                dbContextOptions => dbContextOptions
                    .UseNpgsql(Configuration["DB_CONNECTION"])
                    .EnableSensitiveDataLogging() // <-- These two calls are optional but help
                    .EnableDetailedErrors()       // <-- with debugging (remove for production).
            );
            services.AddHostedService<BaseBackgroundService>();
            services.AddJaeger(Configuration);
            services.AddTransient<BaseService>();
            services.AddSingleton<NameProducer>();
            services.AddSingleton<IMinecraftApiClient, MinecraftApiClient>();
            services.AddSingleton<INameProducer, NameProducer>();
            services.AddHostedService<BaseBackgroundService>();
            services.AddSingleton<HypixelBackgroundService>();
            services.AddHostedService(sp => sp.GetService<HypixelBackgroundService>());
            services.AddScoped<KeyManager>();
            services.AddSingleton<IIpRetriever, IpRetriever>();
            services.AddSingleton<MissingChecker>();
            services.AddSingleton<Coflnet.Sky.Kafka.KafkaCreator>();
            services.AddSingleton<ConnectionMultiplexer>(sp =>
            {
                return  ConnectionMultiplexer.Connect(Configuration["REDIS_HOST"]);
            });

            services.AddResponseCaching();
            services.AddResponseCompression();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "SkyProxy v1");
                c.RoutePrefix = "api";
            });

            app.UseResponseCaching();
            app.UseResponseCompression();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMetrics();
                endpoints.MapControllers();
            });
        }
    }
}
