using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Internal;

namespace Coflnet.Sky.Proxy.Models;

public class CockroachHistoryRepository : NpgsqlHistoryRepository
{
    public CockroachHistoryRepository(HistoryRepositoryDependencies dependencies) : base(dependencies)
    {
    }

    public override IMigrationsDatabaseLock AcquireDatabaseLock()
    {
        return new NoopMigrationsDatabaseLock(this);
    }

    public override Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IMigrationsDatabaseLock>(new NoopMigrationsDatabaseLock(this));
    }

    private sealed class NoopMigrationsDatabaseLock : IMigrationsDatabaseLock
    {
        public NoopMigrationsDatabaseLock(IHistoryRepository historyRepository)
        {
            HistoryRepository = historyRepository;
        }

        public IHistoryRepository HistoryRepository { get; }

        public IMigrationsDatabaseLock ReacquireIfNeeded(bool connectionReopened, bool? transactionRestarted)
        {
            return this;
        }

        public Task<IMigrationsDatabaseLock> ReacquireIfNeededAsync(
            bool connectionReopened,
            bool? transactionRestarted,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IMigrationsDatabaseLock>(this);
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
