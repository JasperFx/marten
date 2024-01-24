using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

public class dictionary_usage: IntegrationContext
{
    private readonly ITestOutputHelper _output;

    public dictionary_usage(DefaultStoreFixture fixture, ITestOutputHelper output): base(fixture)
    {
        _output = output;
    }

    [Fact]
    public async Task playing()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));

        theSession.Logger = new TestOutputMartenLogger(_output);


        var targets = Target.GenerateRandomData(10).ToArray();
        var number = targets.Select(x => x.StringDict).SelectMany(x => x.Values).Count();
        _output.WriteLine(number.ToString());


        await theStore.BulkInsertAsync(targets);
        var data = await theSession.Query<Target>().Select(x => x.StringDict).ToListAsync();

        var count = await theSession.Query<Target>().Where(x => x.StringDict.Any()).Select(x => x.StringDict).ToListAsync();
    }

    // using key0 and value0 for these because the last node, which is deep, should have at least a single dict node


    [Fact]
    public async Task dict_guid_can_query_using_containskey()
    {
        var guid = Guid.NewGuid();
        var target = new Target();
        target.GuidDict.Add(guid, Guid.NewGuid());
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Logger = new TestOutputMartenLogger(_output);

        var results = await theSession.Query<Target>().Where(x => x.GuidDict.ContainsKey(guid)).ToListAsync();
        results.All(r => r.GuidDict.ContainsKey(guid)).ShouldBeTrue();
    }

    [Fact]
    public async Task dict_guid_can_query_using_containsKVP()
    {
        var guidk = Guid.NewGuid();
        var guidv = Guid.NewGuid();
        var target = new Target();
        target.GuidDict.Add(guidk, guidv);
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        var kvp = new KeyValuePair<Guid, Guid>(guidk, guidv);
        // Only works if the dictionary is in interface form
        var results = await theSession.Query<Target>().Where(x => ((IDictionary<Guid, Guid>)x.GuidDict).Contains(kvp))
            .ToListAsync();
        results.All(r => r.GuidDict.Contains(kvp)).ShouldBeTrue();
    }

    [Fact]
    public async Task query_against_values()
    {
        var queryId = Guid.NewGuid();
        var queryId2 = Guid.NewGuid();
        var queryId3 = Guid.NewGuid();
        var guidDict = new Dictionary<Guid, HashSet<Guid>> { { queryId, new HashSet<Guid>() { queryId3 } } };
        var objectDict = new Dictionary<Guid, MyEntity> { { Guid.NewGuid(), new MyEntity(queryId2, string.Empty) } };
        var dictEntity = new DictEntity(Guid.NewGuid(), guidDict, objectDict);

        theSession.Store(dictEntity);
        await theSession.SaveChangesAsync();


        var entityExists = await theSession.Query<DictEntity>()
            .Where(x => x.GuidDict.Values.Any(hs => hs.Contains(queryId3)))
            .AnyAsync();
    }

    [Fact]
    public async Task select_many_against_the_Keys()
    {
        theSession.Logger = new TestOutputMartenLogger(_output);


        await theStore.BulkInsertAsync(Target.GenerateRandomData(1000).ToArray());

        var pairs = await theSession.Query<Target>().SelectMany(x => x.StringDict.Keys).ToListAsync();
        pairs.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task select_many_against_the_values()
    {
        /*
WITH mt_temp_id_list1CTE as (
    select jsonb_path_query(d.data -> 'StringDict', '$.*') ->> 0 as data from public.mt_doc_target as d
)
select data from mt_temp_id_list1CTE
as d where d.data = 'value2' order by d.data;
         */

        theSession.Logger = new TestOutputMartenLogger(_output);

        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));
        var targets = Target.GenerateRandomData(10).ToArray();
        await theStore.BulkInsertAsync(targets);

        var values = await theSession.Query<Target>().SelectMany(x => x.StringDict.Values).ToListAsync();
        values.Count.ShouldBe(targets.SelectMany(x => x.StringDict.Values).Count());
    }


    [Fact]
    public async Task select_many_with_wheres_and_order_by_on_values()
    {
        theSession.Logger = new TestOutputMartenLogger(_output);


        await theStore.BulkInsertAsync(Target.GenerateRandomData(1000).ToArray());

        var values = await theSession.Query<Target>().SelectMany(x => x.StringDict.Values)
            .Where(x => x == "value2")
            .OrderBy(x => x)
            .ToListAsync();

        values.Count.ShouldBeGreaterThan(0);

        /*
WITH mt_temp_id_list1CTE as (
    select jsonb_path_query(d.data -> 'StringDict', '$.*') ->> 0 as data from public.mt_doc_target as d
)
select data from mt_temp_id_list1CTE
as d where d.data = 'value2' order by d.data;
         */
    }

    [Fact]
    public async Task selecting_into_a_dictionary()
    {
        // From GH-2911
        var data = await theSession.Query<SelectDict>().Select(x => x.Dict).ToListAsync();
    }
}

public class EntityWithDict
{
    public Guid Id { get; set; }
    public Dictionary<int, string> Data { get; set; } = new();
}

public sealed record MyEntity(Guid Id, string Value);

public sealed record DictEntity(Guid Id, Dictionary<Guid, HashSet<Guid>> GuidDict,
    Dictionary<Guid, MyEntity> ObjectDict);

public sealed record NestedEntity(Guid Id);
public sealed record SelectDict(Guid Id, Dictionary<Guid, NestedEntity[]> Dict);
