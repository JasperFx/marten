using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_3067_nested_array_in_dictionary : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public record RootRecord(Guid Id, Dictionary<Guid, NestedRecord> Dict);
    public record NestedRecord(List<Guid> Entities);

    public Bug_3067_nested_array_in_dictionary(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task filter_with_dict_list_nested()
    {
        var value = Guid.NewGuid();
        theSession.Store(new RootRecord(Guid.NewGuid(), new Dictionary<Guid, NestedRecord>() { {Guid.NewGuid(), new NestedRecord([value])}, {Guid.NewGuid(), new NestedRecord([Guid.NewGuid()])}, {Guid.NewGuid(), new NestedRecord([Guid.NewGuid()])}}));
        theSession.Store(new RootRecord(Guid.NewGuid(), new Dictionary<Guid, NestedRecord>() { {Guid.NewGuid(), new NestedRecord([Guid.NewGuid()])}, {Guid.NewGuid(), new NestedRecord([Guid.NewGuid()])}}));
        theSession.Store(new RootRecord(Guid.NewGuid(), new Dictionary<Guid, NestedRecord>() { {Guid.NewGuid(), new NestedRecord([Guid.NewGuid()])}, {Guid.NewGuid(), new NestedRecord([Guid.NewGuid()])}}));
        theSession.Store(new RootRecord(Guid.NewGuid(), new Dictionary<Guid, NestedRecord>() { {Guid.NewGuid(), new NestedRecord([Guid.NewGuid()])}, {Guid.NewGuid(), new NestedRecord([Guid.NewGuid()])}}));
        theSession.Store(new RootRecord(Guid.NewGuid(), new Dictionary<Guid, NestedRecord>() { {Guid.NewGuid(), new NestedRecord([Guid.NewGuid()])}, {Guid.NewGuid(), new NestedRecord([Guid.NewGuid()])}}));
        await theSession.SaveChangesAsync();

        theSession.Logger = new TestOutputMartenLogger(_output);

        var ex = await Should.ThrowAsync<BadLinqExpressionException>(async () =>
        {
            var results = await theSession.Query<RootRecord>().
                Where(x => x.Dict.Values.Any(r => r.Entities.Contains(value)))
                .ToListAsync();
        });

        ex.Message.ShouldContain("#>");
    }

    [Fact]
    public async Task selectmany_with_dict_list()
    {
        var value = Guid.Parse("725177c5-f453-46a6-98ae-6b9c6f041b34");
        theSession.Store(new RootRecord(Guid.NewGuid(), new Dictionary<Guid, NestedRecord>() { {Guid.NewGuid(), new NestedRecord([value])},  {Guid.NewGuid(), new NestedRecord([Guid.Empty, ])}, {Guid.NewGuid(), new NestedRecord([Guid.Empty, ])}, {Guid.NewGuid(), new NestedRecord([Guid.Empty, ])}      }));
        theSession.Store(new RootRecord(Guid.NewGuid(), new Dictionary<Guid, NestedRecord>() { {Guid.NewGuid(), new NestedRecord([Guid.Empty, ])}}));
        theSession.Store(new RootRecord(Guid.NewGuid(), new Dictionary<Guid, NestedRecord>() { {Guid.NewGuid(), new NestedRecord([Guid.Empty, ])}}));
        theSession.Store(new RootRecord(Guid.NewGuid(), new Dictionary<Guid, NestedRecord>() { {Guid.NewGuid(), new NestedRecord([Guid.Empty, ])}}));
        theSession.Store(new RootRecord(Guid.NewGuid(), new Dictionary<Guid, NestedRecord>() { {Guid.NewGuid(), new NestedRecord([Guid.Empty, ])}}));
        theSession.Store(new RootRecord(Guid.NewGuid(), new Dictionary<Guid, NestedRecord>() { {Guid.NewGuid(), new NestedRecord([Guid.Empty, ])}}));

        await theSession.SaveChangesAsync();

        var results = await theSession.Query<RootRecord>()
            .SelectMany(x => x.Dict.Values).Where(x => x.Entities.Contains(value))
            .ToListAsync();

        Assert.Single(results);
    }
}
