using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;

namespace LinqTests.Bugs;

public class Bug_xxx_NGram_in_other_schema : BugIntegrationContext
{
    private string AnotherSchema = "not_public";

    [Fact]
    public async Task not_public_schema()
    {
        CleanSchema(AnotherSchema);
        StoreOptions(opts =>
        {
            var documentMappingExpression = opts.Schema.For<DifferentSchema>();
            documentMappingExpression.DatabaseSchemaName(AnotherSchema);
            documentMappingExpression.NgramIndex(x => x.Name);
        }, true);

        var elayne = new Foo { Name = "Elayne", Status = StatusEnum.Active };
        theSession.Store(elayne);
        theSession.Store(new DifferentSchema{ Name = "This is something i would like to find using NGRAM"});
        await theSession.SaveChangesAsync();

        var results = await theSession.Query<DifferentSchema>().Where(x => x.Name.NgramSearch("ing")).ToListAsync();

        results.Any().ShouldBeTrue();
    }

    [Fact]
    public async Task change_default_schema_to_not_public()
    {
        CleanSchema(AnotherSchema);
        StoreOptions(opts =>
        {
            opts.DatabaseSchemaName = AnotherSchema;
            var documentMappingExpression = opts.Schema.For<DifferentSchema>();
            documentMappingExpression.DatabaseSchemaName(AnotherSchema);
            documentMappingExpression.NgramIndex(x => x.Name);
        });

        var elayne = new Foo { Name = "Elayne", Status = StatusEnum.Active };
        theSession.Store(elayne);
        theSession.Store(new DifferentSchema{ Name = "This is something i would like to find using NGRAM"});
        await theSession.SaveChangesAsync();

        var results = await theSession.Query<DifferentSchema>().Where(x => x.Name.NgramSearch("ing")).ToListAsync();

        results.Any().ShouldBeTrue();
    }
}

public class DifferentSchema
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}
