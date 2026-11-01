using System;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Proxy.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Coflnet.Sky.Proxy.Services;

public class DatabaseReadinessTests
{
    [Test]
    public async Task ReadinessQueriesSqlEveryTime()
    {
        var sql = new SqlProbe();
        using var services = Services(sql);
        await DatabaseReadiness.CheckAsync(services);
        await DatabaseReadiness.CheckAsync(services);
        Assert.That(sql.Calls, Is.EqualTo(2));
        Assert.That(sql.CancellationCanBeRequested, Is.True);
    }

    [Test]
    public void DatabaseFailureFailsTheReadinessCheck()
    {
        using var services = Services(new SqlProbe { Fail = true });
        Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseReadiness.CheckAsync(services));
    }

    [Test]
    public async Task StalledSqlIsCancelledWithinTheOverallReadinessDeadline()
    {
        var sql = new SqlProbe { Stall = true };
        using var services = Services(sql);
        var elapsed = Stopwatch.StartNew();
        Assert.CatchAsync<Exception>(() => DatabaseReadiness.CheckAsync(services));
        Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(6)));
        Assert.That(sql.Calls, Is.EqualTo(1));
        await sql.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task CallerCancellationCancelsPendingSql()
    {
        var sql = new SqlProbe { Stall = true };
        using var services = Services(sql);
        using var cancellation = new CancellationTokenSource();
        var check = DatabaseReadiness.CheckAsync(services, cancellation.Token);
        await sql.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(() => check);
        await sql.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task StallIgnoringCancellationIsStillBoundedByTheSafetyNet()
    {
        var sql = new SqlProbe { Stall = true, IgnoreCancellation = true };
        using var services = Services(sql);
        var elapsed = Stopwatch.StartNew();
        Assert.CatchAsync<Exception>(() => DatabaseReadiness.CheckAsync(services));
        Assert.That(elapsed.Elapsed, Is.GreaterThan(TimeSpan.FromSeconds(3)).And.LessThan(TimeSpan.FromSeconds(6)));
        sql.Release.TrySetResult(true);
    }

    [Test]
    public async Task HandlerReturns200WhenSqlWorks()
    {
        using var services = Services(new SqlProbe());
        var (status, body) = await Call(services);
        Assert.That(status, Is.EqualTo(200));
        Assert.That(body, Is.Empty);
    }

    [Test]
    public async Task HandlerReturns503WithoutLeakingTheFailure()
    {
        using var services = Services(new SqlProbe { Fail = true });
        var (status, body) = await Call(services);
        Assert.That(status, Is.EqualTo(503));
        Assert.That(body, Is.Empty);
    }

    [Test]
    public async Task HandlerReturns503WhenSqlStalls()
    {
        using var services = Services(new SqlProbe { Stall = true });
        var elapsed = Stopwatch.StartNew();
        var (status, _) = await Call(services);
        Assert.That(status, Is.EqualTo(503));
        Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(6)));
    }

    private static async Task<(int, string)> Call(ServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        await DatabaseReadiness.HandleAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    private static ServiceProvider Services(SqlProbe sql) => new ServiceCollection()
        .AddDbContext<ProxyDbContext>(options => options
            .UseNpgsql("Host=synthetic.invalid;Username=synthetic;Password=synthetic-only")
            .AddInterceptors(new SuppressOpen(), sql)).BuildServiceProvider();

    private sealed class SuppressOpen : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => new(InterceptionResult.Suppress());
    }

    private sealed class SqlProbe : DbCommandInterceptor
    {
        public bool IgnoreCancellation;
        public TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Stall, Fail, CancellationCanBeRequested;
        public int Calls;
        public TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Assert.That(command.CommandText, Is.EqualTo("SELECT 1"));
            Calls++;
            Started.TrySetResult(true);
            CancellationCanBeRequested = cancellationToken.CanBeCanceled;
            if (Fail) throw new InvalidOperationException("synthetic SQL failure with secret detail");
            if (Stall && IgnoreCancellation)
                await Release.Task;
            else if (Stall)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                finally { if (cancellationToken.IsCancellationRequested) Cancelled.TrySetResult(true); }
            }
            return InterceptionResult<int>.SuppressWithResult(1);
        }
    }
}
