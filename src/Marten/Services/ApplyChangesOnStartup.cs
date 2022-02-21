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
            foreach (PostgresqlDatabase database in Store.Tenancy.BuildDatabases())
            {
                await database.ApplyAllConfiguredChangesToDatabaseAsync(this, AutoCreate.CreateOrUpdate).ConfigureAwait(false);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<bool> TryAttainLock(NpgsqlConnection conn)
        {
            return conn.TryGetGlobalLock(Store.Options.ApplyChangesLockId);
        }

        public Task ReleaseLock(NpgsqlConnection conn)
        {
            return conn.ReleaseGlobalLock(Store.Options.ApplyChangesLockId);
        }
    }
}
