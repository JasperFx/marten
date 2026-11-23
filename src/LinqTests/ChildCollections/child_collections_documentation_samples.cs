using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;

namespace LinqTests.ChildCollections.Samples;

// Compile-only samples for docs/documents/querying/linq/child-collections.md. Nothing here runs as a test;
// the behavior each sample shows is covered by the tests named next to it.

public class Plan
{
    public Guid Id { get; set; }
    public int[][] Grid { get; set; } = [];
    public List<Step> Steps { get; set; } = new();
    public int[] Scores { get; set; } = [];
    public string[] Tags { get; set; } = [];
}

public class Step
{
    public string Name { get; set; } = "";
}

public class Machine
{
    public Guid Id { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
}

public class Team
{
    public Guid Id { get; set; }
    public List<Squad> Squads { get; set; } = new();
}

public class Squad
{
    public string Color { get; set; } = "";
    public List<Player> Players { get; set; } = new();
}

public class Player
{
    public string Name { get; set; } = "";
}

public class child_collections_documentation_samples
{
    // Covered by Bugs/Bug_5949_indexing_document_arrays.cs and ChildCollections/collection_indexing.cs
    #region sample_reading_an_element_by_position
    public async Task reading_an_element_by_position(IQuerySession session)
    {
        var row = 1;

        var hits = await session.Query<Plan>()
            .Where(x => x.Grid[row][0] > 3 && x.Steps[0].Name == "start")
            .OrderBy(x => x.Scores[2])
            .Select(x => new { x.Id, First = x.Tags[0], Cell = x.Grid[1][1] })
            .ToListAsync();

        // ElementAt() reads by position too
        var second = await session.Query<Plan>().Where(x => x.Steps.ElementAt(1).Name == "build").ToListAsync();
    }
    #endregion

    // Covered by ChildCollections/querying_through_n_deep_sub_collections.cs and
    // ChildCollections/Bug_5549_nested_any_containment_locator.cs
    #region sample_nested_any
    public async Task nested_any(IQuerySession session)
    {
        // A team with a green squad that has a player named Bill
        var teams = await session.Query<Team>()
            .Where(x => x.Squads.Any(s => s.Color == "Green" && s.Players.Any(p => p.Name == "Bill")))
            .ToListAsync();

        // A team with any squad that has any player at all
        var staffed = await session.Query<Team>()
            .Where(x => x.Squads.Any(s => s.Players.Any()))
            .ToListAsync();
    }
    #endregion

    // Covered by Acceptance/where_clauses.cs, Acceptance/dictionary_usage.cs,
    // Bugs/dictionary_key_sql_injection.cs and Bugs/Bug_5063_ordering_by_dictionary_indexer.cs
    #region sample_querying_dictionaries
    public async Task querying_dictionaries(IQuerySession session)
    {
        // Target.StringDict is an IDictionary<string, string>
        var hasKey = await session.Query<Target>().Where(x => x.StringDict.ContainsKey("key0")).ToListAsync();
        var hasValue = await session.Query<Target>().Where(x => x.StringDict.Values.Contains("value3"))
            .ToListAsync();
        var someKey = await session.Query<Target>().Where(x => x.StringDict.Keys.Any(k => k.EndsWith("3")))
            .ToListAsync();
        var someEntry = await session.Query<Target>().Where(x => x.StringDict.Any(p => p.Value == "value2"))
            .ToListAsync();
        var bigOnes = await session.Query<Target>().Where(x => x.StringDict.Count > 2).ToListAsync();

        // Contains() of a whole entry needs the IDictionary<TKey, TValue> interface
        var pair = new KeyValuePair<string, string>("key0", "value0");
        var hasPair = await session.Query<Target>().Where(x => x.StringDict.Contains(pair)).ToListAsync();

        // Flatten the keys or the values of every document
        var allValues = await session.Query<Target>().SelectMany(x => x.StringDict.Values).ToListAsync();

        // Machine.Tags is a Dictionary<string, string>. The indexer reads one entry,
        // in Where() and in OrderBy()
        var redMachines = await session.Query<Machine>()
            .Where(x => x.Tags["color"] == "red")
            .OrderBy(x => x.Tags["zone"])
            .ToListAsync();
    }
    #endregion
}
