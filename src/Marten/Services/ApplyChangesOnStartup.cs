using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;

namespace Marten.Services
{
    internal class ApplyChangesOnStartup<T>: ApplyChangesOnStartup where T : IDocumentStore
    {
        public ApplyChangesOnStartup(T store) : base(store)
        {
        }
    }

    internal class ApplyChangesOnStartup : IHostedService, IGlobalLock<NpgsqlConnection>
    {
        public DocumentStore Store { get; }

        public ApplyChangesOnStartup(IDocumentStore store)
        {
            Store = store.As<DocumentStore>();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var databases = Store.Tenancy.BuildDatabases().ConfigureAwait(false);
            foreach (PostgresqlDatabase database in await databases)
            {
                await database.ApplyAllConfiguredChangesToDatabaseAsync(this, AutoCreate.CreateOrUpdate).ConfigureAwait(false);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task<bool> TryAttainLock(NpgsqlConnection conn)
        {
            if (await conn.TryGetGlobalLock(Store.Options.ApplyChangesLockId).ConfigureAwait(false)) return true;
            await Task.Delay(50).ConfigureAwait(false);
            if (await conn.TryGetGlobalLock(Store.Options.ApplyChangesLockId).ConfigureAwait(false)) return true;
            await Task.Delay(100).ConfigureAwait(false);
            if (await conn.TryGetGlobalLock(Store.Options.ApplyChangesLockId).ConfigureAwait(false)) return true;
            await Task.Delay(250).ConfigureAwait(false);
            if (await conn.TryGetGlobalLock(Store.Options.ApplyChangesLockId).ConfigureAwait(false)) return true;

            return false;
        }

        public Task ReleaseLock(NpgsqlConnection conn)
        {
            return conn.ReleaseGlobalLock(Store.Options.ApplyChangesLockId);
        }
    }
}
