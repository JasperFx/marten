using Marten.Testing.Harness;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Xunit;

namespace DocumentDbTests.Bugs;

public class MixedContainsIsOneOf: IntegrationContext
{
    public MixedContainsIsOneOf(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task mixing_contains_and_isoneof_breaks_where_parsing()
    {
        var guid = Guid.NewGuid();
        theSession.Store(new MyDocument(Guid.NewGuid(), new List<Guid>() { guid }));
        await theSession.SaveChangesAsync();

        var items = await theSession.Query<MyDocument>().Where(x => guid.IsOneOf(x.Actions)).ToListAsync();
        Assert.Equal(1, items.Count);

        var items2 = await theSession.Query<MyDocument>().Where(x => x.Actions.Contains(guid)).ToListAsync();
        Assert.Equal(1, items2.Count);
    }
    public record MyDocument(Guid Id, List<Guid> Actions);
}
