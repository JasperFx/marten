using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;

namespace LinqTests.Bugs;

// #5287: `hashSet.Contains(x.EnumMember)` threw InvalidCastException out of Npgsql --
// "Writing values of 'Colors[]' is not supported for parameters having NpgsqlDbType
// '-2147483639'" -- because HashSetEnumerableContains handed the raw EnumType[] straight
// to a `= ANY(?)` parameter. Its two sibling parsers already project the constant
// collection through EnumIsOneOfWhereFragment for exactly this reason: EnumerableContains
// (the array and List shapes, #2946) and MemoryExtensionsContains (net10's span binding,
// #4610). This is the same fix in the third parser.
public class Bug_5287_hashset_of_enums_contains: BugIntegrationContext
{
    public record ScalarDoc(string Id, Colors Color);

    private async Task seedAsync()
    {
        theSession.Store(
            new ScalarDoc("one", Colors.Blue),
            new ScalarDoc("two", Colors.Red),
            new ScalarDoc("three", Colors.Green));

        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task hashset_of_enums_contains_member_as_int()
    {
        StoreOptions(opts => opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger));
        await seedAsync();

        var wanted = new HashSet<Colors> { Colors.Blue, Colors.Red };

        var results = await theSession.Query<ScalarDoc>()
            .Where(x => wanted.Contains(x.Color))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

        results.ShouldBe(new[] { "one", "two" });
    }

    [Fact]
    public async Task hashset_of_enums_contains_member_as_string()
    {
        StoreOptions(opts => opts.UseSystemTextJsonForSerialization(EnumStorage.AsString));
        await seedAsync();

        var wanted = new HashSet<Colors> { Colors.Blue, Colors.Red };

        var results = await theSession.Query<ScalarDoc>()
            .Where(x => wanted.Contains(x.Color))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

        results.ShouldBe(new[] { "one", "two" });
    }

    [Fact]
    public async Task hashset_of_enums_matching_nothing_returns_nothing()
    {
        StoreOptions(opts => opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger));
        await seedAsync();

        var wanted = new HashSet<Colors> { Colors.Orange };

        var results = await theSession.Query<ScalarDoc>()
            .Where(x => wanted.Contains(x.Color))
            .Select(x => x.Id)
            .ToListAsync();

        results.ShouldBeEmpty();
    }

    // The non-enum path through the same parser has to keep working -- it never went through
    // EnumIsOneOfWhereFragment and must not start now.
    [Fact]
    public async Task hashset_of_strings_contains_member()
    {
        StoreOptions(opts => opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger));
        await seedAsync();

        var wanted = new HashSet<string> { "one", "two" };

        var results = await theSession.Query<ScalarDoc>()
            .Where(x => wanted.Contains(x.Id))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

        results.ShouldBe(new[] { "one", "two" });
    }

    // The shapes that already worked, pinned here beside the one that did not so the family
    // stays together.
    [Fact]
    public async Task array_and_list_of_enums_still_work()
    {
        StoreOptions(opts => opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger));
        await seedAsync();

        var wantedArray = new[] { Colors.Blue, Colors.Red };
        var byArray = await theSession.Query<ScalarDoc>()
            .Where(x => wantedArray.Contains(x.Color))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

        byArray.ShouldBe(new[] { "one", "two" });

        var wantedList = new List<Colors> { Colors.Blue, Colors.Red };
        var byList = await theSession.Query<ScalarDoc>()
            .Where(x => wantedList.Contains(x.Color))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

        byList.ShouldBe(new[] { "one", "two" });
    }
}
