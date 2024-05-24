using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Includes;

public class includes_with_filtering_on_included_documents: IntegrationContext
{
    public static readonly Target[] Data = Target.GenerateRandomData(1000).ToArray();

    public includes_with_filtering_on_included_documents(DefaultStoreFixture fixture) : base(fixture)
    {

    }

    protected override async Task fixtureSetup()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(TargetHolder));

        await theStore.BulkInsertAsync(Data);

        var holders = Data.Select(x => new TargetHolder { TargetId = x.Id }).ToArray();
        await theStore.BulkInsertAsync(holders);
    }

    [Fact]
    public async Task filter_included_documents_to_list()
    {
        var list = new List<Target>();

        var holders = await theSession.Query<TargetHolder>()
            .Include<Target>(x => x.TargetId, list, t => t.Color == Colors.Green)
            .ToListAsync();

        list.Select(x => x.Color).Distinct()
            .Single().ShouldBe(Colors.Green);

        list.Count.ShouldBe(Data.Count(x => x.Color == Colors.Green));
    }

    #region sample_filter_included_documents

    [Fact]
    public async Task filter_included_documents_to_lambda()
    {
        var list = new List<Target>();

        var holders = await theSession.Query<TargetHolder>()
            .Include(list).On(x => x.TargetId, t => t.Color == Colors.Blue)
            .ToListAsync();

        list.Select(x => x.Color).Distinct()
            .Single().ShouldBe(Colors.Blue);

        list.Count.ShouldBe(Data.Count(x => x.Color == Colors.Blue));
    }

    #endregion

    [Fact]
    public async Task filter_included_documents_to_dictionary()
    {
        var dict = new Dictionary<Guid, Target>();

        var holders = await theSession.Query<TargetHolder>()
            .Include<Target, Guid>(x => x.TargetId, dict, t => t.Color == Colors.Blue)
            .ToListAsync();

        dict.Values.Select(x => x.Color).Distinct()
            .Single().ShouldBe(Colors.Blue);

        dict.Count.ShouldBe(Data.Count(x => x.Color == Colors.Blue));
    }


    [Fact]
    public async Task filter_included_documents_from_mutiple_identities_on_parent()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(ManyTargetHolder));

        var holder1 = new ManyTargetHolder { TargetIds = new List<Guid>(Data.Select(x => x.Id).Take(250)) };
        var holder2 = new ManyTargetHolder { TargetIds = new List<Guid>(Data.Select(x => x.Id).Skip(250).Take(250)) };
        var holder3 = new ManyTargetHolder { TargetIds = new List<Guid>(Data.Select(x => x.Id).Skip(500).Take(250)) };
        var holder4 = new ManyTargetHolder { TargetIds = new List<Guid>(Data.Select(x => x.Id).Skip(750)) };

        await theStore.BulkInsertAsync(new ManyTargetHolder[] { holder1, holder2, holder3, holder4 });

        var list = new List<Target>();

        var holders = await theSession.Query<ManyTargetHolder>()
            .Include<Target>(x => x.TargetIds, x => list.Add(x), t => t.Color == Colors.Blue)
            .ToListAsync();

        list.Select(x => x.Color).Distinct()
            .Single().ShouldBe(Colors.Blue);

        list.Count.ShouldBe(Data.Count(x => x.Color == Colors.Blue));
    }

    [Fact]
    public async Task filter_included_documents_to_list_with_compiled_query()
    {
        var query = new FilterIncludeCompiledQuery { Color = Colors.Green };
        var holders = await theSession.QueryAsync(query);

        query.Targets.Select(x => x.Color).Distinct()
            .Single().ShouldBe(Colors.Green);

        query.Targets.Count.ShouldBe(Data.Count(x => x.Color == Colors.Green));
    }
}

public class FilterIncludeCompiledQuery: ICompiledListQuery<TargetHolder>
{
    public Expression<Func<IMartenQueryable<TargetHolder>, IEnumerable<TargetHolder>>> QueryIs()
    {
        return q => q.Include<Target>(x => x.TargetId, Targets, t => t.Color == Color).OrderBy(x => x.Id);
    }

    public List<Target> Targets { get; set; } = new();

    public Colors Color { get; set; } = Colors.Blue;
}

public class TargetHolder
{
    public Guid Id { get; set; }
    public Guid TargetId { get; set; }
}

public class ManyTargetHolder
{
    public Guid Id { get; set; }
    public List<Guid> TargetIds { get; set; } = new();
}


