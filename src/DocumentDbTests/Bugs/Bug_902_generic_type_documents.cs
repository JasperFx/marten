using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_902_generic_type_documents: IntegrationContext
{
    [Fact]
    public async Task can_create_object_name()
    {
        var doc2 = new MartenStoredState<Dictionary<string, string>>
        {
            Value = new Dictionary<string, string> { { "color", "blue" } }
        };

        using (var session = theStore.LightweightSession())
        {
            session.Store(doc2);
            await session.SaveChangesAsync();
        }

        using (var query = theStore.QuerySession())
        {
            query.Load<MartenStoredState<Dictionary<string, string>>>(doc2.Id)
                .Value["color"].ShouldBe("blue");
        }
    }

    public Bug_902_generic_type_documents(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}

public class MartenStoredState<T>
{
    public Guid Id = Guid.NewGuid();

    public T Value { get; set; }
}
