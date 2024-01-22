using System;
using Marten.Schema;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class GenericTypeToPersist<T>
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public class TypeA { }

public class TypeB { }

public class ability_to_persist_generic_types: BugIntegrationContext
{
    [Fact]
    public void can_persist_and_load_generic_types()
    {
        var doc1A = new GenericTypeToPersist<TypeA>();
        var doc1B = new GenericTypeToPersist<TypeB>();

        TheSession.Store(doc1A);
        TheSession.Store(doc1B);
        TheSession.SaveChanges();

        var doc2A = TheSession.Load<GenericTypeToPersist<TypeA>>(doc1A.Id);
        var doc2B = TheSession.Load<GenericTypeToPersist<TypeA>>(doc2A.Id);
        doc2A.ShouldNotBeNull();
        doc2B.ShouldNotBeNull();
    }

    public ability_to_persist_generic_types()
    {
    }
}

public class ability_to_persist_nested_generic_types: BugIntegrationContext
{
    [Fact]
    public void can_persist_and_load_generic_types()
    {
        var doc1 = new NestedGenericTypeToPersist<TypeA>();

        TheSession.Store(doc1);
        TheSession.SaveChanges();

        var doc2 = TheSession.Load<NestedGenericTypeToPersist<TypeA>>(doc1.Id);
        doc2.ShouldNotBeNull();
    }

    [DocumentAlias("nested_generic")]
    public class NestedGenericTypeToPersist<T>
    {
        public Guid Id = Guid.NewGuid();
    }

}
