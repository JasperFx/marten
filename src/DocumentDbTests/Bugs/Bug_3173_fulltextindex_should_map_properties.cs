using System.Threading;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_3173_fulltextindex_should_map_properties: BugIntegrationContext
{
    [Fact]
    public async Task create_full_text_index_with_property_mapping()
    {
        StoreOptions(o =>
        {
            o.RegisterDocumentType<FullTextIndexDoc>();
            o.Schema.For<FullTextIndexDoc>().FullTextIndex(x => x.Value);
        });

        theSession.Store(new FullTextIndexDoc("Id-1", "Val-1"));
        await theSession.SaveChangesAsync();

        // use an orm like Dapper in Marten <7.0 to run the same query
        var indexDefinitions = await theSession.AdvancedSqlQueryAsync<string>(
            $"""
             select indexdef
             from pg_catalog.pg_indexes
             where schemaname = '{SchemaName}'
             and indexname like '%\_idx\_fts' escape '\'
             and indexname like ('%' || ? || '%' ) escape '\'
             """, CancellationToken.None,
            theSession.DocumentStore.Options.Schema.For<FullTextIndexDoc>(false).Replace("_", "\\_"));

        var def = Assert.Single(indexDefinitions);
        Assert.Contains("(data ->> 'Value'::text)", def);
    }
}

public record FullTextIndexDoc(string Id, string Value);
