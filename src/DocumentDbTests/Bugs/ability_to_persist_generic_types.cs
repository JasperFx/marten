using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class GenericTypeToPersist<T>
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public class TypeA
{
}

public class TypeB
{
}

public class ability_to_persist_generic_types: BugIntegrationContext
{
    [Fact]
    public async Task can_persist_and_load_generic_types()
    {
        var doc1A = new GenericTypeToPersist<TypeA>();
        var doc1B = new GenericTypeToPersist<TypeB>();

        theSession.Store(doc1A);
        theSession.Store(doc1B);
        await theSession.SaveChangesAsync();

        var doc2A = await theSession.LoadAsync<GenericTypeToPersist<TypeA>>(doc1A.Id);
        var doc2B = await theSession.LoadAsync<GenericTypeToPersist<TypeA>>(doc2A.Id);
        doc2A.ShouldNotBeNull();
        doc2B.ShouldNotBeNull();
    }
}

public class ability_to_persist_nested_generic_types: BugIntegrationContext
{
    [Fact]
    public async Task can_persist_and_load_generic_types()
    {
        var doc1 = new NestedGenericTypeToPersist<TypeA>();

        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        var doc2 = await theSession.LoadAsync<NestedGenericTypeToPersist<TypeA>>(doc1.Id);
        doc2.ShouldNotBeNull();
    }

    [DocumentAlias("nested_generic")]
    public class NestedGenericTypeToPersist<T>
    {
        public Guid Id = Guid.NewGuid();
    }
}
