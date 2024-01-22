using System;
using Marten.Services;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

// This was to address GH-86
public class ability_to_persist_nested_types_Tests: BugIntegrationContext
{
    [Fact]
    public void can_persist_and_load_nested_types()
    {
        var doc1 = new MyDocument();

        TheSession.Store(doc1);
        TheSession.SaveChanges();

        var doc2 = TheSession.Load<MyDocument>(doc1.Id);
        doc2.ShouldNotBeNull();
    }

    public class MyDocument
    {
        public Guid Id = Guid.NewGuid();
    }

}
