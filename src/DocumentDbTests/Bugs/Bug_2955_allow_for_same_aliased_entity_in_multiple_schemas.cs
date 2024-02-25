using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_2955_allow_for_same_aliased_entity_in_multiple_schemas : BugIntegrationContext
{
    [Fact]
    public async Task allow_usage_of_same_alias_in_different_schemas()
    {
        StoreOptions(opts =>
        {
            opts.RegisterDocumentType<Doc1>();
            opts.RegisterDocumentType<Doc2>();
        });

        theSession.Store(new Doc1());
        theSession.Store(new Doc2());
        await theSession.SaveChangesAsync();
    }
}

[DocumentAlias("samurai"), DatabaseSchemaName("one")]
public class Doc1
{
    public Guid Id { get; set; }
}

[DocumentAlias("samurai"), DatabaseSchemaName("two")]
public class Doc2
{
    public Guid Id { get; set; }
}

