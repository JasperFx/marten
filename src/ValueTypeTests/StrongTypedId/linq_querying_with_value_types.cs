using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using StronglyTypedIds;

namespace ValueTypeTests.StrongTypedId;

public class linq_querying_with_value_types: OneOffConfigurationsContext
{
    public linq_querying_with_value_types()
    {
        StoreOptions(opts =>
        {
            // opts is a StoreOptions just like you'd have in
            // AddMarten() calls
            opts.RegisterValueType(typeof(GuidId));
            opts.RegisterValueType(typeof(Description));
            opts.RegisterValueType(typeof(UpperLimit));
            opts.RegisterValueType(typeof(LowerLimit));
        });
    }

    [Fact]
    public async Task store_several_and_use_in_LINQ_order_by()
    {
        var commonParentId = new GuidId(Guid.NewGuid());
        var doc1 = new LimitedDoc { ParentId = commonParentId, Lower = new LowerLimit(1), Upper = new UpperLimit(20), Description = new Description("desc1") };
        var doc2 = new LimitedDoc { Lower = new LowerLimit(5), Upper = new UpperLimit(25), Description = new Description("desc3") };
        var doc3 = new LimitedDoc { Lower = new LowerLimit(4), Upper = new UpperLimit(15), Description = new Description("desc2") };
        var doc4 = new LimitedDoc { ParentId = commonParentId, Lower = new LowerLimit(3), Upper = new UpperLimit(10), Description = new Description("desc4") };

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

    [Fact]
    public async Task store_several_and_use_in_LINQ_where_clause()
    {
        var commonParentId = new GuidId(Guid.NewGuid());
        var doc1 = new LimitedDoc { ParentId = commonParentId, Lower = new LowerLimit(1), Upper = new UpperLimit(20), Description = new Description("desc1") };
        var doc2 = new LimitedDoc { Lower = new LowerLimit(5), Upper = new UpperLimit(25), Description = new Description("desc3") };
        var doc3 = new LimitedDoc { Lower = new LowerLimit(4), Upper = null, Description = null };
        var doc4 = new LimitedDoc { ParentId = commonParentId, Lower = new LowerLimit(3), Upper = new UpperLimit(10), Description = new Description("desc4") };

        theSession.Store(doc1, doc2, doc3, doc4);
        await theSession.SaveChangesAsync();

        var filteredByIntBasedValueType = await theSession
            .Query<LimitedDoc>()
            .OrderBy(x => x.Lower)
            .Where(x => x.Lower == new LowerLimit(3) || x.Upper == null)
            .Select(x => x.Id)
            .ToListAsync();

        filteredByIntBasedValueType.ShouldHaveTheSameElementsAs(doc4.Id, doc3.Id);

        var filteredByStringBasedValueType = await theSession
            .Query<LimitedDoc>()
            .OrderBy(x => x.Description)
            .Where(x => x.Description == new Description("desc3") || x.Description == null)
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
        var commonParentId = new GuidId(Guid.NewGuid());
        var doc1 = new LimitedDoc { ParentId = commonParentId, Lower = new LowerLimit(1), Upper = new UpperLimit(20), Description = new Description("desc1") };
        var doc2 = new LimitedDoc { Lower = new LowerLimit(5), Upper = new UpperLimit(25), Description = new Description("desc3") };
        var doc3 = new LimitedDoc { Lower = new LowerLimit(4), Upper = new UpperLimit(15), Description = new Description("desc2") };
        var doc4 = new LimitedDoc { ParentId = commonParentId, Lower = new LowerLimit(3), Upper = new UpperLimit(10), Description = new Description("desc4") };

        theSession.Store(doc1, doc2, doc3, doc4);
        await theSession.SaveChangesAsync();

        var intBased = await theSession
            .Query<LimitedDoc>()
            .Where(x =>
                x.Upper.IsOneOf(new UpperLimit(15), new UpperLimit(25))
                || x.Lower.IsOneOf(new LowerLimit(3), new LowerLimit(4)))
            .OrderBy(x => x.Lower)
            .Select(x => x.Id)
            .ToListAsync();

        intBased.ShouldHaveTheSameElementsAs(doc4.Id, doc3.Id, doc2.Id);

        var stringBased = await theSession
            .Query<LimitedDoc>()
            .Where(x =>
                x.Description.IsOneOf(new Description("desc1"), new Description("desc4")))
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
        var parentId1 = new GuidId(Guid.NewGuid());
        var parentId2 = new GuidId(Guid.NewGuid());
        var doc1 = new LimitedDoc { ParentId = parentId1, Lower = new LowerLimit(1), Upper = new UpperLimit(20), Description = new Description("desc1") };
        var doc2 = new LimitedDoc { Lower = new LowerLimit(5), Upper = new UpperLimit(25), Description = new Description("desc3") };
        var doc3 = new LimitedDoc { Lower = new LowerLimit(4), Upper = new UpperLimit(15), Description = new Description("desc2") };
        var doc4 = new LimitedDoc { ParentId = parentId2, Lower = new LowerLimit(3), Upper = new UpperLimit(10), Description = new Description("desc4") };

        theSession.Store(doc1, doc2, doc3, doc4);
        await theSession.SaveChangesAsync();

        var intBased = await theSession
            .Query<LimitedDoc>()
            .Where(x =>
                x.Upper.IsOneOf(new UpperLimit(15), new UpperLimit(25)))
            .OrderBy(x => x.Lower)
            .Select(x => x.Lower)
            .ToListAsync();

        intBased.ShouldHaveTheSameElementsAs(new LowerLimit(4), new LowerLimit(5));

        var longBased = await theSession
            .Query<LimitedDoc>()
            .Where(x =>
                x.Upper.IsOneOf(new UpperLimit(20), new UpperLimit(25)))
            .OrderBy(x => x.Upper)
            .Select(x => x.Upper)
            .ToListAsync();

        longBased.ShouldHaveTheSameElementsAs(new UpperLimit(20), new UpperLimit(25));

        var stringBased = await theSession
            .Query<LimitedDoc>()
            .Where(x =>
                x.Description.IsOneOf(new Description("desc1"), new Description("desc4")))
            .OrderBy(x => x.Description)
            .Select(x => x.Description)
            .ToListAsync();

        stringBased.ShouldHaveTheSameElementsAs(new Description("desc1"), new Description("desc4"));

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

[StronglyTypedId(Template.Long)]
public partial struct UpperLimit;

[StronglyTypedId(Template.Int)]
public partial struct LowerLimit;

[StronglyTypedId(Template.String)]
public partial struct Description;

[StronglyTypedId(Template.Guid)]
public partial struct GuidId;

public class LimitedDoc
{
    public Guid Id { get; set; }
    public GuidId? ParentId { get; set; }
    public UpperLimit? Upper { get; set; }
    public LowerLimit Lower { get; set; }
    public Description? Description { get; set; }
}
