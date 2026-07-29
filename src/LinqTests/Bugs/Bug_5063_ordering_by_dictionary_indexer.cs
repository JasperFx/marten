using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

/// <summary>
/// #5063 — OrderBy against a dictionary indexer used to drop the key and sort by the whole
/// JSON object, so every key produced an identical ordering.
///
/// Every test here deliberately orders by a key that is <em>not</em> the first one in the
/// dictionary. jsonb compares objects key by key, so sorting the whole object happens to
/// agree with sorting by the first key — a test written against that key passes with or
/// without the fix and proves nothing. That is exactly why the original defect survived.
/// </summary>
public class Bug_5063_ordering_by_dictionary_indexer: IntegrationContext
{
    public Bug_5063_ordering_by_dictionary_indexer(DefaultStoreFixture fixture): base(fixture)
    {
    }

    public class Machine
    {
        public Guid Id { get; set; }

        // "rank" sorts opposite to "zone", and "primary" opposite to "secondary".
        public Dictionary<string, string> Tags { get; set; } = new();

        public Dictionary<string, int> Readings { get; set; } = new();
    }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(Machine)];

    private async Task seed()
    {
        var machines = new[]
        {
            new Machine
            {
                Tags = { { "rank", "1" }, { "zone", "d" } },
                Readings = { { "primary", 10 }, { "secondary", 400 } }
            },
            new Machine
            {
                Tags = { { "rank", "2" }, { "zone", "c" } },
                Readings = { { "primary", 20 }, { "secondary", 300 } }
            },
            new Machine
            {
                Tags = { { "rank", "3" }, { "zone", "b" } },
                Readings = { { "primary", 30 }, { "secondary", 200 } }
            },
            new Machine
            {
                Tags = { { "rank", "4" }, { "zone", "a" } },
                Readings = { { "primary", 40 }, { "secondary", 100 } }
            }
        };

        theSession.Store(machines);
        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task orders_by_the_requested_key_not_the_whole_dictionary()
    {
        await seed();
        await using var query = theStore.QuerySession();

        var byZone = await query.Query<Machine>().OrderBy(x => x.Tags["zone"]).ToListAsync();
        byZone.Select(x => x.Tags["zone"]).ShouldBe(new[] { "a", "b", "c", "d" });

        // Same documents, different key, opposite order. Before the fix these two queries
        // returned byte-identical orderings because the key never reached the ORDER BY.
        var byRank = await query.Query<Machine>().OrderBy(x => x.Tags["rank"]).ToListAsync();
        byRank.Select(x => x.Tags["rank"]).ShouldBe(new[] { "1", "2", "3", "4" });

        byZone.Select(x => x.Id).ShouldNotBe(byRank.Select(x => x.Id));
    }

    [Fact]
    public async Task orders_descending_by_the_requested_key()
    {
        await seed();
        await using var query = theStore.QuerySession();

        var results = await query.Query<Machine>().OrderByDescending(x => x.Tags["zone"]).ToListAsync();
        results.Select(x => x.Tags["zone"]).ShouldBe(new[] { "d", "c", "b", "a" });
    }

    [Fact]
    public async Task orders_by_a_non_string_dictionary_value()
    {
        await seed();
        await using var query = theStore.QuerySession();

        // Numeric values must also be cast, otherwise they sort lexically.
        var results = await query.Query<Machine>().OrderBy(x => x.Readings["secondary"]).ToListAsync();
        results.Select(x => x.Readings["secondary"]).ShouldBe(new[] { 100, 200, 300, 400 });
    }
}
