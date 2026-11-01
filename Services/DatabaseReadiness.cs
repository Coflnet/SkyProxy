using System;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Proxy.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.Proxy.Services;

/// <summary>
/// Readiness probe that proves a SQL round trip works with the current (possibly renewed) credentials.
/// </summary>
internal static class DatabaseReadiness
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(4);

    /// <summary>Endpoint handler: 200 when SQL answers in time, otherwise a bare 503 (no exception details).</summary>
    public static async Task HandleAsync(HttpContext context)
    {
        try
        {
            await CheckAsync(context.RequestServices, context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status200OK;
        }
        catch (Exception)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
    }

    public static async Task CheckAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Deadline);
        try
        {
            await Task.Run(async () =>
            {
                using var scope = services.CreateScope();
                var database = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
                await database.Database.ExecuteSqlRawAsync("SELECT 1", timeout.Token);
            }, timeout.Token).WaitAsync(Deadline, cancellationToken);
        }
        finally { timeout.Cancel(); }
    }
}
