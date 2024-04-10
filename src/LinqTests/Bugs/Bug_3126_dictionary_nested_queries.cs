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

public class Bug_3126_dictionary_nested_queries : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public record DictObject(Guid Id, Dictionary<Guid, HashSet<Guid>> GuidDict, Dictionary<Guid, NestedObjectWithinDict> NestedObject);
    public record NestedObjectWithinDict(Guid Id);

    public Bug_3126_dictionary_nested_queries(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task broken_linq_condition_3()
    {
        var id = Guid.Parse("49a5c1ff-cdef-40d4-9b53-26d1f5454810");

        var o = new DictObject(Guid.NewGuid(), new Dictionary<Guid, HashSet<Guid>>(),
            new Dictionary<Guid, NestedObjectWithinDict>());

        o.NestedObject.Add(Guid.NewGuid(), new NestedObjectWithinDict(Guid.NewGuid()));
        o.NestedObject.Add(Guid.NewGuid(), new NestedObjectWithinDict(Guid.NewGuid()));
        o.NestedObject.Add(Guid.NewGuid(), new NestedObjectWithinDict(Guid.NewGuid()));

        var main = new DictObject(Guid.NewGuid(), new Dictionary<Guid, HashSet<Guid>>(),
            new Dictionary<Guid, NestedObjectWithinDict>());

        main.NestedObject.Add(Guid.NewGuid(), new NestedObjectWithinDict(id));
        main.GuidDict.Add(id, new HashSet<Guid>{Guid.NewGuid(), Guid.NewGuid()});

        await theStore.BulkInsertDocumentsAsync([o, main]);

        theSession.Logger = new TestOutputMartenLogger(_output);

        await Should.ThrowAsync<BadLinqExpressionException>(async () =>
        {
            await theSession.Query<DictObject>()
                .Where(x => x.NestedObject.Values.Any(s => s.Id == id)).ToListAsync();
        });


    }
}
