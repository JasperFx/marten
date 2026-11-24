using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using IInitialData = Marten.Schema.IInitialData;
using JasperFx.Core;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
///     #6194: MartenActivator copied <c>ResourceMigrationFailureMode</c> onto Weasel's database. Under
///     <c>ContinueOnFailures</c>, Weasel answers a migration lock it cannot attain with "nothing to do", so a lost
///     lock read as a completed migration and the seeders ran against a schema another node was still migrating.
///     JasperFx's StoreActivator expects Weasel left at FailFast: the lost lock then reaches it as a failure, which
///     it logs and continues past without seeding.
/// </summary>
public class a_lost_migration_lock_skips_the_seeders
{
    private const string Schema = "bug_6194_lost_lock";

    [Fact]
    public async Task starts_without_seeding_when_another_node_holds_the_migration_lock()
    {
        var lockId = Random.Shared.Next(100_000, 1_000_000);

        // A fresh schema, so the migration has work to do and asks for the lock
        await using (var cleaner = DocumentStore.For(opts =>
                     {
                         opts.Connection(ConnectionSource.ConnectionString);
                         opts.DatabaseSchemaName = Schema;
                     }))
        {
            await cleaner.Advanced.Clean.CompletelyRemoveAllAsync();
        }

        // Another node, mid-migration
        await using var holder = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await holder.OpenAsync();
        await using (var cmd = new NpgsqlCommand($"select pg_advisory_lock({lockId})", holder))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        var seeder = new RecordingSeeder();

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = Schema;
                        opts.RegisterDocumentType<Target>();
                        opts.ApplyChangesLockId = lockId;
                        opts.ApplyChangesLockTimeout = 250.Milliseconds();
                        opts.ResourceMigrationFailureMode = ResourceMigrationFailureMode.ContinueOnFailures;
                    })
                    .ApplyAllDatabaseChangesOnStartup()
                    .InitializeWith(seeder);
            })
            .StartAsync();

        seeder.Populated.ShouldBe(0);
    }

    private sealed class RecordingSeeder: IInitialData
    {
        public int Populated;

        public Task Populate(IDocumentStore store, CancellationToken cancellation)
        {
            Interlocked.Increment(ref Populated);
            return Task.CompletedTask;
        }
    }
}
