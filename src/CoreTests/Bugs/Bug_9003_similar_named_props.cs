#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_9003_similar_named_props: BugIntegrationContext
{
    [Fact]
    public async Task where_clause_with_similar_names_should_still_be_different()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.RegisterDocumentType<Document>();
        });

        var doc1 = new Document(Guid.NewGuid(), new Foo("aaa"), new FooBar("bbb"));
        var doc2 = new Document(Guid.NewGuid(), new Foo("ccc"), new FooBar("ddd"));

        theSession.Store(doc1);
        theSession.Store(doc2);
        await theSession.SaveChangesAsync();

        // expected: should match both documents
        var lookup = await theSession.Query<Document>()
            .Where(x => x.Foo.BarBaz == "aaa" || x.FooBar.Baz == "ddd")
            .ToListAsync();

        // actual query:
        //   select d.id, d.data
        //   from bugs.mt_doc_bug_9003_similar_named_props_document as d
        //   where (d.tenant_id = $1  and  (d.data -> 'Foo' ->> 'BarBaz' = $2 or d.data -> 'Foo' ->> 'BarBaz' = $3))
        //                                                            incorrect--^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
        // parameters: $1 = '*DEFAULT*', $2 = 'aaa', $3 = 'ddd'

        // query should be:
        //   select d.id, d.data
        //   from bugs.mt_doc_bug_9003_similar_named_props_document as d
        //   where (d.tenant_id = $1  and  (d.data -> 'Foo' ->> 'BarBaz' = $2 or d.data -> 'FooBar' ->> 'Baz' = $3))
        //                                                                       ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
        // parameters: $1 = '*DEFAULT*', $2 = 'aaa', $3 = 'ddd'

        Assert.Equal(2, lookup.Count);
        Assert.Contains(lookup, x => x.Id == doc1.Id);
        Assert.Contains(lookup, x => x.Id == doc2.Id);
    }

    public record Document(Guid Id, Foo Foo, FooBar FooBar);

    public record Foo(string BarBaz);

    public record FooBar(string Baz);
}
