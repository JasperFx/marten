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
    [Fact]
    public async Task use_the_query()
    {
        StoreOptions(opts =>
        {
            var documentMappingExpression = opts.Schema.For<DifferentSchema>();
            documentMappingExpression.DatabaseSchemaName("not_public");
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
