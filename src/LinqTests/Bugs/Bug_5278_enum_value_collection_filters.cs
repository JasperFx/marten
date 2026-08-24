// `using System;` is load-bearing for this repro: it brings the
// System.MemoryExtensions.Contains(span, value, comparer) extension into scope, which
// wins overload resolution over Enumerable.Contains for enum arrays (enums do not
// implement IEquatable<T>). Real applications get it implicitly via <ImplicitUsings>.
using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;

namespace LinqTests.Bugs;

// Companion to Bug 2946: that fix covered Contains(Colors.Blue) only when the
// expression binds to Enumerable.Contains. With `using System;`, enum arrays bind to
// MemoryExtensions.Contains(..., comparer: null), so the old parser read the null
// comparer as the search value and generated {"Colors":[null]}. Any(c => c == value)
// crashed the parser with IndexOutOfRangeException.
public class Bug_5278_enum_value_collection_filters : BugIntegrationContext
{
    public record MyDoc(string Id, Colors[] Colors);

    private async Task seedAsync()
    {
        var doc1 = new MyDoc("one", [Colors.Blue, Colors.Green]);
        var doc2 = new MyDoc("two", [Colors.Blue, Colors.Red]);
        var doc3 = new MyDoc("three", [Colors.Orange, Colors.Yellow]);

        theSession.Store(doc1, doc2, doc3);
        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task contains_with_literal_as_int_when_system_namespace_is_in_scope()
    {
        StoreOptions(opts => opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger));
        await seedAsync();

        var results = await theSession.Query<MyDoc>()
            .Where(x => x.Colors.Contains(Colors.Blue))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

        results.ShouldBe(new string[] { "one", "two" });
    }

    [Fact]
    public async Task contains_with_captured_variable_as_int()
    {
        StoreOptions(opts => opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger));
        await seedAsync();

        var color = Colors.Blue;
        var results = await theSession.Query<MyDoc>()
            .Where(x => x.Colors.Contains(color))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

        results.ShouldBe(new string[] { "one", "two" });
    }

    [Fact]
    public async Task contains_with_captured_variable_as_string()
    {
        StoreOptions(opts => opts.UseSystemTextJsonForSerialization(EnumStorage.AsString));
        await seedAsync();

        var color = Colors.Blue;
        var results = await theSession.Query<MyDoc>()
            .Where(x => x.Colors.Contains(color))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

        results.ShouldBe(new string[] { "one", "two" });
    }

    [Fact]
    public async Task any_with_equality_on_literal_as_int()
    {
        StoreOptions(opts => opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger));
        await seedAsync();

        var results = await theSession.Query<MyDoc>()
            .Where(x => x.Colors.Any(c => c == Colors.Blue))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

        results.ShouldBe(new string[] { "one", "two" });
    }

    [Fact]
    public async Task any_with_equality_on_captured_variable_as_int()
    {
        StoreOptions(opts => opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger));
        await seedAsync();

        var color = Colors.Blue;
        var results = await theSession.Query<MyDoc>()
            .Where(x => x.Colors.Any(c => c == color))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

        results.ShouldBe(new string[] { "one", "two" });
    }
}
