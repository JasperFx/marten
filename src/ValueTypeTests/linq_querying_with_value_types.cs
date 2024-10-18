using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Vogen;


namespace ValueTypeTests;

public class linq_querying_with_value_types : OneOffConfigurationsContext
{
    public linq_querying_with_value_types()
    {
        StoreOptions(opts =>
        {
            #region sample_registering_value_types

            // opts is a StoreOptions just like you'd have in
            // AddMarten() calls
            opts.RegisterValueType(typeof(UpperLimit));
            opts.RegisterValueType(typeof(LowerLimit));

            #endregion
        });
    }

    #region sample_using_value_type_in_linq

    [Fact]
    public async Task store_several_and_order_by()
    {
        var doc1 = new LimitedDoc { Lower = LowerLimit.From(1), Upper = UpperLimit.From(20) };
        var doc2 = new LimitedDoc { Lower = LowerLimit.From(5), Upper = UpperLimit.From(25) };
        var doc3 = new LimitedDoc { Lower = LowerLimit.From(4), Upper = UpperLimit.From(15) };
        var doc4 = new LimitedDoc { Lower = LowerLimit.From(3), Upper = UpperLimit.From(10) };

        theSession.Store(doc1, doc2, doc3, doc4);
        await theSession.SaveChangesAsync();

        var ordered = await theSession
            .Query<LimitedDoc>()
            .OrderBy(x => x.Lower)
            .Select(x => x.Id)
            .ToListAsync();

        ordered.ShouldHaveTheSameElementsAs(doc1.Id, doc4.Id, doc3.Id, doc2.Id);
    }

    #endregion

    [Fact]
    public async Task store_several_and_query_by()
    {
        var doc1 = new LimitedDoc { Lower = LowerLimit.From(1), Upper = UpperLimit.From(20) };
        var doc2 = new LimitedDoc { Lower = LowerLimit.From(5), Upper = UpperLimit.From(25) };
        var doc3 = new LimitedDoc { Lower = LowerLimit.From(4), Upper = UpperLimit.From(15) };
        var doc4 = new LimitedDoc { Lower = LowerLimit.From(3), Upper = UpperLimit.From(10) };

        theSession.Store(doc1, doc2, doc3, doc4);
        await theSession.SaveChangesAsync();

        var ordered = await theSession
            .Query<LimitedDoc>()
            .OrderBy(x => x.Lower)
            .Where(x => x.Upper == UpperLimit.From(10))
            .Select(x => x.Id)
            .ToListAsync();

        ordered.ShouldHaveTheSameElementsAs(doc4.Id);
    }

    [Fact]
    public async Task use_value_type_as_parameter_in_compiled_query()
    {
        var doc1 = new LimitedDoc { Lower = LowerLimit.From(1), Upper = UpperLimit.From(20) };
        var doc2 = new LimitedDoc { Lower = LowerLimit.From(5), Upper = UpperLimit.From(25) };
        var doc3 = new LimitedDoc { Lower = LowerLimit.From(4), Upper = UpperLimit.From(15) };
        var doc4 = new LimitedDoc { Lower = LowerLimit.From(3), Upper = UpperLimit.From(10) };

        theSession.Store(doc1, doc2, doc3, doc4);
        await theSession.SaveChangesAsync();

        var raw = await theSession.QueryAsync(new LimitedDocQuery());
        var ordered = raw.Select(x => x.Id).ToArray();

        ordered.ShouldHaveTheSameElementsAs(doc4.Id);
    }
}

public class LimitedDocQuery: ICompiledListQuery<LimitedDoc>
{
    public Expression<Func<IMartenQueryable<LimitedDoc>, IEnumerable<LimitedDoc>>> QueryIs()
    {
        return query => query.OrderBy(x => x.Lower)
            .Where(x => x.Upper == Upper);
    }

    public UpperLimit Upper { get; set; } = UpperLimit.From(10);
}

#region sample_limited_doc

[ValueObject<int>]
public partial struct UpperLimit;

[ValueObject<int>]
public partial struct LowerLimit;

public class LimitedDoc
{
    public Guid Id { get; set; }
    public UpperLimit Upper { get; set; }
    public LowerLimit Lower { get; set; }
}

#endregion

