using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;

namespace LinqTests.Bugs;

public class Bug_2946_querying_on_value_collection_of_enums_serialized_as_string : BugIntegrationContext
{
    public record MyDoc(string Id, Colors[] Colors);

    [Fact]
    public async Task try_to_query_on_contains_as_string()
    {
        StoreOptions(opts => opts.UseDefaultSerialization(EnumStorage.AsString));

        var doc1 = new MyDoc("one", new Colors[] { Colors.Blue, Colors.Green });
        var doc2 = new MyDoc("two", new Colors[] { Colors.Blue, Colors.Red });
        var doc3 = new MyDoc("three", new Colors[] { Colors.Orange, Colors.Yellow });

        theSession.Store(doc1, doc2, doc3);
        await theSession.SaveChangesAsync();

        var results = await theSession.Query<MyDoc>()
            .Where(x => x.Colors.Contains(Colors.Blue))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

        results.ShouldBe(new string[]{"one", "two"});
    }

    [Fact]
    public async Task try_to_query_on_contains_as_int()
    {
        StoreOptions(opts => opts.UseDefaultSerialization(EnumStorage.AsInteger));

        var doc1 = new MyDoc("one", new Colors[] { Colors.Blue, Colors.Green });
        var doc2 = new MyDoc("two", new Colors[] { Colors.Blue, Colors.Red });
        var doc3 = new MyDoc("three", new Colors[] { Colors.Orange, Colors.Yellow });

        theSession.Store(doc1, doc2, doc3);
        await theSession.SaveChangesAsync();

        var results = await theSession.Query<MyDoc>()
            .Where(x => x.Colors.Contains(Colors.Blue))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

        results.ShouldBe(new string[]{"one", "two"});
    }
}
