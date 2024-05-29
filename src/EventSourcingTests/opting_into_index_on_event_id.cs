using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql.Tables;
using Xunit;

namespace EventSourcingTests;

public class opting_into_index_on_event_id : OneOffConfigurationsContext
{
    public static async Task sample_usage()
    {
        #region sample_using_optional_event_store_indexes

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection("some connection string");

            // Add the unique index to the id field
            opts.Events.EnableUniqueIndexOnEventId = true;
        });

        #endregion

    }

    [Fact]
    public async Task opt_into_the_event()
    {
        StoreOptions(opts =>
        {
            opts.Events.EnableUniqueIndexOnEventId = true;
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();


        var table = new Table(new DbObjectName(SchemaName, "mt_events"));
        var existing = await table.FetchExistingAsync(conn);

        existing.Indexes.Any(x => x.Name == "idx_mt_events_event_id")
            .ShouldBeTrue();
    }
}
