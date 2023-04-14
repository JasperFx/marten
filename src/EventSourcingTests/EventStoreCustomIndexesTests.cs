using System.Linq;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Testing;
using Marten.Testing.Harness;
using Weasel.Postgresql.Tables;
using Xunit;

namespace EventSourcingTests;

public class EventStoreCustomIndexesTests : OneOffConfigurationsContext
{
    [Fact]
    public async Task can_create_custom_indexes_on_event_tables()
    {
        const string streamsTypeIndexName = "idx_mt_streams_type";
        const string eventsDataIndexName = "idx_mt_events_data_gin";
        StoreOptions(options =>
        {
            var streamsTypeIndex = new IndexDefinition(streamsTypeIndexName).AgainstColumns("type");
            options.Events.AddIndexToStreamsTable(streamsTypeIndex);

            var eventsDataIndex = new IndexDefinition(eventsDataIndexName).AgainstColumns("data");
            eventsDataIndex.Method = IndexMethod.gin;
            options.Events.AddIndexToEventsTable(eventsDataIndex);
        });

        await theStore.EnsureStorageExistsAsync(typeof(IEvent));

        Assert.True(await CheckIfIndexExists("mt_streams", streamsTypeIndexName));
        Assert.True(await CheckIfIndexExists("mt_events", eventsDataIndexName));
    }

    private async Task<bool> CheckIfIndexExists(string tableName, string indexName)
    {
        var exists = await theSession.QueryAsync<bool>(@"
            select exists(
	            select 1
	            from pg_catalog.pg_indexes 
	            where schemaname = ?
	              and tablename = ?
	              and indexname = ?
            )",
            _schemaName,
            tableName,
            indexName);

        return exists.FirstOrDefault(false);
    }
}
