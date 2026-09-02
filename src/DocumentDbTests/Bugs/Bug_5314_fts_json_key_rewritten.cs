using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_5314_fts_json_key_rewritten: BugIntegrationContext
{
    public class FtsDoc
    {
        public Guid Id { get; set; }
        public string Data { get; set; }
        public string Title { get; set; }
    }

    public Bug_5314_fts_json_key_rewritten()
    {
        // camelCase serialization is what makes this reachable: it turns the Data member into the
        // JSON key "data", which the old substring rebase rewrote along with the column.
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(casing: Casing.CamelCase);
            opts.Schema.For<FtsDoc>().FullTextIndex(x => x.Data, x => x.Title);
        });
    }

    [Fact]
    public void the_json_key_is_not_rebased_onto_the_table_alias()
    {
        using var session = theStore.QuerySession();

        var sql = session.Query<FtsDoc>().Where(x => x.PlainTextSearch("hi")).ToCommand().CommandText;

        sql.ShouldContain("d.data ->> 'data'");
        sql.ShouldNotContain("'d.data'");
    }

    [Fact]
    public async Task can_search_on_a_member_whose_serialized_name_contains_data()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(FtsDoc));

        await using var session = theStore.LightweightSession();
        session.Store(
            new FtsDoc { Id = Guid.NewGuid(), Data = "kangaroo", Title = "irrelevant" },
            new FtsDoc { Id = Guid.NewGuid(), Data = "irrelevant", Title = "kangaroo" });
        await session.SaveChangesAsync();

        var results = await session.Query<FtsDoc>().Where(x => x.PlainTextSearch("kangaroo")).ToListAsync();

        results.Count.ShouldBe(2);
    }
}
