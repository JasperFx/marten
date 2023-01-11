using Marten.Testing.Harness;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Xunit;

namespace DocumentDbTests.Bugs;

public class incorrect_method_parsing : IntegrationContext
{
    public incorrect_method_parsing(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task correct_parser_should_be_picked_for_given_where_clause_scenario_1()
    {
        var guid = Guid.NewGuid();
        theSession.Store(new MyDocument(Guid.NewGuid(), new List<Guid>() { guid }));
        await theSession.SaveChangesAsync();

        // IsInGenericEnumerable added to _methodParsing hashmap
        var items = await theSession.Query<MyDocument>().Where(x => guid.IsOneOf(x.Actions)).ToListAsync();
        Assert.Equal(1, items.Count);

        // EnumerableContains should be used here, but IsInGenericEnumerable is picked instead
        var items2 = await theSession.Query<MyDocument>().Where(x => x.Actions.Contains(guid)).ToListAsync();
        Assert.Equal(1, items2.Count);
    }
    public record MyDocument(Guid Id, List<Guid> Actions);

    [Fact]
    public async Task correct_parser_should_be_picked_for_given_where_clause_scenario_2()
    {
        var guids = new List<Guid>() {Guid.NewGuid(), Guid.NewGuid() };
        var internalguid = Guid.NewGuid();
        theSession.Store(new MyDocument(guids[0], new List<Guid>() { internalguid }));
        await theSession.SaveChangesAsync();

        // IsInGenericEnumerable added to _methodParsing hashmap
        var items = await theSession.Query<MyDocument>().Where(x => guids.Contains(x.Id)).ToListAsync();
        Assert.Equal(1, items.Count);

        // EnumerableContains should be used here, but IsInGenericEnumerable is picked instead
        var items2 = await theSession.Query<MyDocument>().Where(x => x.Actions.Contains(internalguid)).ToListAsync();
        Assert.Equal(1, items2.Count);
    }
}
