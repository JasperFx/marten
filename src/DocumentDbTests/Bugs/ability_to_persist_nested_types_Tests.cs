using System;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

// This was to address GH-86
public class ability_to_persist_nested_types_Tests: BugIntegrationContext
{
    [Fact]
    public async Task can_persist_and_load_nested_types()
    {
        var doc1 = new MyDocument();

        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        var doc2 = await theSession.LoadAsync<MyDocument>(doc1.Id);
        doc2.ShouldNotBeNull();
    }

    public class MyDocument
    {
        public Guid Id = Guid.NewGuid();
    }

}
