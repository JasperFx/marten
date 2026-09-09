#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Storage;
using Npgsql;
using Shouldly;
using Xunit;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// #5364 — `db-apply --parallel` across many databases intermittently died with
/// <c>InvalidOperationException: Collection was modified; enumeration operation may not execute</c>
/// out of <c>PerTenantEventSequences.currentPartitionSuffixes()</c>.
///
/// <para>
/// The defect was upstream (weasel#583): one <c>ManagedListPartitions</c> instance is shared by
/// every database in a store, it mutated its partition dictionary IN PLACE, and published it
/// through a <c>ReadOnlyDictionary</c> that wraps rather than copies. So
/// <c>InitializeAsync</c>'s <c>Clear()</c>-and-refill ran while another database's worker was
/// enumerating the same dictionary. Weasel 9.31.1 publishes a snapshot swapped copy-on-write;
/// Marten needs no product change, which is exactly what this test pins.
/// </para>
///
/// <para>
/// Both halves below are production code. The writer is
/// <c>ManagedListPartitions.InitializeAsync</c>, which <c>TenantPartitionsDatabaseInitializer</c>
/// runs at the head of every migration operation. The reader is verbatim the body of Weasel's
/// <c>DatabaseBase.assertValidIdentifiers</c>, which runs at the very TOP of
/// <c>ApplyAllConfiguredChangesToDatabaseAsync</c> — before a connection is even opened, and
/// therefore before this database's own partition view has been hydrated, which is what drops the
/// read onto the shared store-wide fallback.
/// </para>
///
/// <para>
/// The two are driven directly rather than through a pair of real parallel applies because the
/// end-to-end shape needs the reporter's scale to trip: the collision window is the span of one
/// <c>Clear()</c>-and-refill, and it only opens for a worker that STARTS LATE (workers that begin
/// together all finish reading before anyone writes). At the reporter's 29 databases /
/// <c>--parallel 16</c> the bounded scheduler supplies those late starters and the failure showed
/// up once per run; three shard databases with a handful of tenants each do not, and a test that
/// only fails on someone else's hardware guards nothing.
/// </para>
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class Bug_5364_concurrent_apply_partition_race: ShardedPartitionedContext
{
    public Bug_5364_concurrent_apply_partition_race(ShardedPartitionedFixture fixture): base(fixture)
    {
    }

    private DocumentStore FreshStore() => BuildShardedStore(opts =>
    {
        opts.AutoCreateSchemaObjects = AutoCreate.All;
        opts.Events.AddEventType<ShardedTestEvent>();
    });

    [Fact]
    public async Task enumerating_schema_object_names_while_the_partition_initializer_runs()
    {
        var token = TestContext.Current.CancellationToken;

        // Seed: create the schema on every shard, then register enough tenants that the registry
        // refill below is more than one row wide.
        var seed = FreshStore();
        foreach (var db in (await seed.Options.Tenancy.BuildDatabases()).OfType<IMartenDatabase>())
        {
            await db.ApplyAllConfiguredChangesToDatabaseAsync(ct: token);
        }

        for (var i = 0; i < 40; i++)
        {
            await seed.Advanced.AddTenantToShardAsync($"tenant{i:D3}", token);
        }

        Exception? captured = null;

        // A store instance hydrates the shared store-wide snapshot at most once, so each round
        // needs a fresh store to get one more attempt at the window.
        for (var round = 0; round < 40 && captured == null; round++)
        {
            using var store = FreshStore();
            var databases = (await store.Options.Tenancy.BuildDatabases()).OfType<IMartenDatabase>().ToArray();
            var partitions = store.Options.TenantPartitions!.Partitions;

            using var stop = new CancellationTokenSource();

            var readers = databases.Skip(1).Select(db => Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        foreach (var schemaObject in db.AllObjects())
                        {
                            foreach (var _ in schemaObject.AllNames())
                            {
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Interlocked.CompareExchange(ref captured, e, null);
                        return;
                    }
                }
            }, token)).ToArray();

            await using (var conn = new NpgsqlConnection(Fixture.ConnectionStrings.Values.First()))
            {
                await conn.OpenAsync(token);
                await partitions.InitializeAsync(conn, token);
                await conn.CloseAsync();
            }

            await Task.Delay(50, token);
            await stop.CancelAsync();
            await Task.WhenAll(readers);
        }

        captured.ShouldBeNull();
    }
}
