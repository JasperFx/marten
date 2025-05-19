using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Vogen;

namespace ValueTypeTests.VogenIds;

public class linq_querying_with_value_types : OneOffConfigurationsContext
{
    public linq_querying_with_value_types()
    {
        StoreOptions(opts =>
        {
            #region sample_registering_value_types

            // opts is a StoreOptions just like you'd have in
            // AddMarten() calls
            opts.RegisterValueType(typeof(GuidId));
            opts.RegisterValueType(typeof(UpperLimit));
            opts.RegisterValueType(typeof(LowerLimit));
            opts.RegisterValueType(typeof(Description));

            #endregion
        });
    }

    #region sample_using_value_type_in_linq

    [Fact]
    public async Task store_several_and_use_in_LINQ_order_by()
    {
        var commonParentId = GuidId.From(Guid.NewGuid());
        var doc1 = new LimitedDoc { ParentId = commonParentId, Lower = LowerLimit.From(1), Upper = UpperLimit.From(20), Description = Description.From("desc1") };
        var doc2 = new LimitedDoc { Lower = LowerLimit.From(5), Upper = UpperLimit.From(25), Description = Description.From("desc3") };
        var doc3 = new LimitedDoc { Lower = LowerLimit.From(4), Upper = UpperLimit.From(15), Description = Description.From("desc2") };
        var doc4 = new LimitedDoc { ParentId = commonParentId, Lower = LowerLimit.From(3), Upper = UpperLimit.From(10), Description = Description.From("desc4") };

        theSession.Store(doc1, doc2, doc3, doc4);
        await theSession.SaveChangesAsync();

        var orderedByIntBased = await theSession
            .Query<LimitedDoc>()
            .OrderBy(x => x.Lower)
            .Select(x => x.Id)
            .ToListAsync();

        orderedByIntBased.ShouldHaveTheSameElementsAs(doc1.Id, doc4.Id, doc3.Id, doc2.Id);

        var orderedByLongBased = await theSession
            .Query<LimitedDoc>()
            .OrderBy(x => x.Upper)
            .Select(x => x.Id)
            .ToListAsync();

        orderedByLongBased.ShouldHaveTheSameElementsAs(doc4.Id, doc3.Id, doc1.Id, doc2.Id);

        var orderedByStringBased = await theSession
            .Query<LimitedDoc>()
            .OrderBy(x => x.Description)
            .Select(x => x.Id)
            .ToListAsync();

        orderedByStringBased.ShouldHaveTheSameElementsAs(doc1.Id, doc3.Id, doc2.Id, doc4.Id);

        var orderedByGuidBased = await theSession
            .Query<LimitedDoc>()
            .OrderBy(x => x.ParentId)
            .Select(x => x.Id)
            .ToListAsync();

        orderedByGuidBased.ShouldHaveTheSameElementsAs(doc1.Id, doc4.Id, doc2.Id, doc3.Id);
    }

    #endregion

    [Fact]
    public async Task store_several_and_use_in_LINQ_where_clause()
    {
        var commonParentId = GuidId.From(Guid.NewGuid());
        var doc1 = new LimitedDoc { ParentId = commonParentId, Lower = LowerLimit.From(1), Upper = UpperLimit.From(20), Description = Description.From("desc1") };
        var doc2 = new LimitedDoc { Lower = LowerLimit.From(5), Upper = UpperLimit.From(25), Description = Description.From("desc3") };
        var doc3 = new LimitedDoc { Lower = LowerLimit.From(4), Upper = null, Description = null };
        var doc4 = new LimitedDoc { ParentId = commonParentId, Lower = LowerLimit.From(3), Upper = UpperLimit.From(10), Description =Description.From("desc4") };

        theSession.Store(doc1, doc2, doc3, doc4);
        await theSession.SaveChangesAsync();

        var filteredByIntBasedValueType = await theSession
            .Query<LimitedDoc>()
            .OrderBy(x => x.Lower)
            .Where(x => x.Lower == LowerLimit.From(3) || x.Upper == null)
            .Select(x => x.Id)
            .ToListAsync();

        filteredByIntBasedValueType.ShouldHaveTheSameElementsAs(doc4.Id, doc3.Id);

        var filteredByStringBasedValueType = await theSession
            .Query<LimitedDoc>()
            .OrderBy(x => x.Description)
            .Where(x => x.Description == Description.From("desc3") || x.Description == null)
            .Select(x => x.Id)
            .ToListAsync();

        filteredByStringBasedValueType.ShouldHaveTheSameElementsAs(doc2.Id, doc3.Id);

        var filteredByGuidBasedValueType = await theSession
            .Query<LimitedDoc>()
            .OrderBy(x => x.Lower)
            .Where(x => x.ParentId == commonParentId)
            .Select(x => x.Id)
            .ToListAsync();

        filteredByGuidBasedValueType.ShouldHaveTheSameElementsAs(doc1.Id, doc4.Id);
    }

    [Fact]
    public async Task store_several_and_use_in_LINQ_is_one_of()
    {
        var commonParentId = GuidId.From(Guid.NewGuid());
        var doc1 = new LimitedDoc { ParentId = commonParentId, Lower = LowerLimit.From(1), Upper = UpperLimit.From(20), Description = Description.From("desc1") };
        var doc2 = new LimitedDoc { Lower = LowerLimit.From(5), Upper = UpperLimit.From(25), Description = Description.From("desc3") };
        var doc3 = new LimitedDoc { Lower = LowerLimit.From(4), Upper = UpperLimit.From(15), Description = Description.From("desc2") };
        var doc4 = new LimitedDoc { ParentId = commonParentId, Lower = LowerLimit.From(3), Upper = UpperLimit.From(10), Description = Description.From("desc4") };

        theSession.Store(doc1, doc2, doc3, doc4);
        await theSession.SaveChangesAsync();

        var intBased = await theSession
            .Query<LimitedDoc>()
            .Where(x =>
                x.Upper.IsOneOf(UpperLimit.From(15), UpperLimit.From(25))
                || x.Lower.IsOneOf(LowerLimit.From(3), LowerLimit.From(4)))
            .OrderBy(x => x.Lower)
            .Select(x => x.Id)
            .ToListAsync();

        intBased.ShouldHaveTheSameElementsAs(doc4.Id, doc3.Id, doc2.Id);

        var stringBased = await theSession
            .Query<LimitedDoc>()
            .Where(x =>
                x.Description.IsOneOf(Description.From("desc1"), Description.From("desc4")))
            .OrderBy(x => x.Description)
            .Select(x => x.Id)
            .ToListAsync();

        stringBased.ShouldHaveTheSameElementsAs(doc1.Id, doc4.Id);

        var guidBased = await theSession
            .Query<LimitedDoc>()
            .Where(x =>
                x.ParentId.IsOneOf(commonParentId))
            .OrderBy(x => x.Description)
            .Select(x => x.Id)
            .ToListAsync();

        guidBased.ShouldHaveTheSameElementsAs(doc1.Id, doc4.Id);
    }

    [Fact]
    public async Task store_several_and_use_in_LINQ_select()
    {
        var parentId1 = GuidId.From(Guid.NewGuid());
        var parentId2 = GuidId.From(Guid.NewGuid());
        var doc1 = new LimitedDoc { ParentId = parentId1, Lower = LowerLimit.From(1), Upper = UpperLimit.From(20), Description = Description.From("desc1") };
        var doc2 = new LimitedDoc { Lower = LowerLimit.From(5), Upper = UpperLimit.From(25), Description = Description.From("desc3") };
        var doc3 = new LimitedDoc { Lower = LowerLimit.From(4), Upper = UpperLimit.From(15), Description = Description.From("desc2") };
        var doc4 = new LimitedDoc { ParentId = parentId2, Lower = LowerLimit.From(3), Upper = UpperLimit.From(10), Description = Description.From("desc4") };

        theSession.Store(doc1, doc2, doc3, doc4);
        await theSession.SaveChangesAsync();

        var intBased = await theSession
            .Query<LimitedDoc>()
            .Where(x =>
                x.Upper.IsOneOf(UpperLimit.From(15), UpperLimit.From(25)))
            .OrderBy(x => x.Lower)
            .Select(x => x.Lower)
            .ToListAsync();

        intBased.ShouldHaveTheSameElementsAs(LowerLimit.From(4), LowerLimit.From(5));

        var longBased = await theSession
            .Query<LimitedDoc>()
            .Where(x =>
                x.Upper.IsOneOf(UpperLimit.From(20), UpperLimit.From(25)))
            .OrderBy(x => x.Upper)
            .Select(x => x.Upper)
            .ToListAsync();

        longBased.ShouldHaveTheSameElementsAs(UpperLimit.From(20), UpperLimit.From(25));

        var stringBased = await theSession
            .Query<LimitedDoc>()
            .Where(x =>
                x.Description.IsOneOf(Description.From("desc1"), Description.From("desc4")))
            .OrderBy(x => x.Description)
            .Select(x => x.Description)
            .ToListAsync();

        stringBased.ShouldHaveTheSameElementsAs(Description.From("desc1"), Description.From("desc4"));

        var guidBased = await theSession
            .Query<LimitedDoc>()
            .Where(x =>
                x.ParentId.IsOneOf(parentId1, parentId2))
            .OrderBy(x => x.Description)
            .Select(x => x.ParentId)
            .ToListAsync();

        guidBased.ShouldHaveTheSameElementsAs(parentId1, parentId2);
    }
}

#region sample_limited_doc

[ValueObject<long>]
public partial struct UpperLimit;

[ValueObject<int>]
public partial struct LowerLimit;

[ValueObject<string>]
public partial struct Description;

[ValueObject<Guid>]
public partial struct GuidId;

public class LimitedDoc
{
    public Guid Id { get; set; }

    public GuidId? ParentId { get; set; }
    public UpperLimit? Upper { get; set; }
    public LowerLimit Lower { get; set; }
    public Description? Description { get; set; }
}

#endregion

