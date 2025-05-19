using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_3708_NGram_in_other_schema : BugIntegrationContext
{
    private string AnotherSchema = "not_public";

    [Fact]
    public async Task default_bug_context_schema()
    {
        StoreOptions(opts =>
        {
            var documentMappingExpression = opts.Schema.For<NGramDifferentSchema>();
            documentMappingExpression.NgramIndex(x => x.Name);
        });

        theSession.Store(new NGramDifferentSchema{ Name = "This is something i would like to find using NGRAM"});
        await theSession.SaveChangesAsync();

        var results = await theSession.Query<NGramDifferentSchema>().Where(x => x.Name.NgramSearch("ing")).ToListAsync();

        results.Any().ShouldBeTrue();
    }

    [Fact]
    public async Task not_public_schema()
    {
        var store = DocumentStore.For(_ =>
        {
            _.Connection(Marten.Testing.Harness.ConnectionSource.ConnectionString);
            _.DatabaseSchemaName = AnotherSchema;
            _.Schema.For<NGramDifferentSchema>().NgramIndex(x => x.Name);
        });

        await using var session = store.LightweightSession();

        session.Store(new NGramDifferentSchema{ Name = "This is something i would like to find using NGRAM"});
        await session.SaveChangesAsync();

        var results = await session.Query<NGramDifferentSchema>().Where(x => x.Name.NgramSearch("ing")).ToListAsync();

        results.Any().ShouldBeTrue();
    }
}

public class NGramDifferentSchema
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}
