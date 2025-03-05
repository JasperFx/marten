using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq.MatchesSql;
using Marten.Schema;
using Marten.Testing.Harness;
using NpgsqlTypes;
using Weasel.Postgresql.Tables;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_PR_3702_list_index_compile_error : BugIntegrationContext
{
    [Fact]
    public async Task can_create_array_duplicate_column_on_a_list_field()
    {
        StoreOptions(opts =>
        {
            opts.RegisterDocumentType<DocWithIndexOnList>();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var newDoc = new DocWithIndexOnList { Id = Guid.NewGuid(), ListOfStrings = ["foo", "bar", "baz"] };
        theSession.Store(newDoc);
        await theSession.SaveChangesAsync();

        List<string> arrayFilter = ["foo", "baz"];
        var queriedDoc = await theSession.Query<DocWithIndexOnList>()
            .Where(x => x.MatchesSql("d.list_of_strings @> ?", arrayFilter))
            .FirstOrDefaultAsync();

        Assert.Equal(newDoc.Id, queriedDoc.Id);
    }

    public class DocWithIndexOnList
    {
        public Guid Id { get; set; }

        [DuplicateField(DbType = NpgsqlDbType.Array | NpgsqlDbType.Text, PgType = "text[]", IndexMethod = IndexMethod.gin)]
        public List<string> ListOfStrings { get; set; }
    }
}


