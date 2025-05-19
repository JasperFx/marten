using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

public class negation_operator : IntegrationContext
{
    [Fact]
    public async Task negating_predicate_with_an_and_operator_results_in_a_correct_query()
    {
        var player1 = new Player {Name = "Tony", Level = 10};
        var player2 = new Player {Name = "Mark", Level = 20};
        var player3 = new Player {Name = "Steve", Level = 10};
        var player4 = new Player {Name = "Leeroy", Level = 20};

        theSession.Store(player1, player2, player3, player4);
        await theSession.SaveChangesAsync();

        using var query = theStore.QuerySession();

        var players = query.Query<Player>()
            .Where(c => !(c.Name == "Tony" && c.Level == 10))
            .ToArray();

        players.Count(x => new[] { player2.Id, player3.Id, player4.Id }.Contains(x.Id)).ShouldBe(3);
    }

    [Fact]
    public async Task negating_predicate_with_an_or_operator_results_in_a_correct_query()
    {
        var player1 = new Player { Name = "Tony", Level = 10};
        var player2 = new Player { Name = "Mark", Level = 20};
        var player3 = new Player { Name = "Steve", Level = 10};
        var player4 = new Player { Name = "Leeroy", Level = 20};

        theSession.Store(player1, player2, player3, player4);
        await theSession.SaveChangesAsync();

        using var query = theStore.QuerySession();

        var players = query.Query<Player>()
            .Where(c => !(c.Name == "Tony" || c.Level == 10))
            .ToArray();

        players.Count(x => new[] { player2.Id, player4.Id }.Contains(x.Id)).ShouldBe(2);
    }

    public negation_operator(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}

public class Player
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public int Level { get; set; }
}
