using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Marten;
using Marten.Linq.CursorPaging;
using Marten.Testing.Harness;
using Shouldly;

namespace ValueTypeTests.VogenIds;

public class cursor_paging_with_value_type_ids: OneOffConfigurationsContext
{
    private static readonly int[] Ranks = [1, 2, 2, 2, 3];

    public cursor_paging_with_value_type_ids()
    {
        StoreOptions(opts =>
        {
            opts.RegisterValueType<GuidId>();
            opts.RegisterValueType<IntId>();
            opts.RegisterValueType<Description>();
            opts.Schema.For<FluentIdentityValueTypeDoc>().Identity(x => x.Key);
            opts.Schema.For<DuplicatedValueTypeKeyDoc>().Duplicate(x => x.Key);
        });
    }

    [Fact]
    public async Task pages_through_all_documents_when_the_id_member_is_a_value_type()
    {
        theSession.Store(Ranks.Select(rank => new ValueTypeIdDoc { Id = newId(), Rank = rank }).ToArray());
        await theSession.SaveChangesAsync();

        var ordered = theSession.Query<ValueTypeIdDoc>().OrderByDescending(x => x.Rank).ThenByDescending(x => x.Id);

        var expected = await ordered.Select(x => x.Id).ToListAsync();
        var paged = await allPages<ValueTypeIdDoc, GuidId>(
            cursor => ordered.ToJsonPageByCursorAsync(cursor, pageSize: 2), x => x.Id);

        paged.ShouldBe(expected);
    }

    [Fact]
    public async Task pages_through_all_documents_when_the_identity_attribute_is_on_a_value_type()
    {
        theSession.Store(Ranks.Select(rank => new AttributeIdentityValueTypeDoc { Key = newId(), Rank = rank }).ToArray());
        await theSession.SaveChangesAsync();

        var ordered = theSession.Query<AttributeIdentityValueTypeDoc>().OrderByDescending(x => x.Rank).ThenByDescending(x => x.Key);

        var expected = await ordered.Select(x => x.Key).ToListAsync();
        var paged = await allPages<AttributeIdentityValueTypeDoc, GuidId>(
            cursor => ordered.ToJsonPageByCursorAsync(cursor, pageSize: 2), x => x.Key);

        paged.ShouldBe(expected);
    }

    [Fact]
    public async Task pages_through_all_documents_when_the_configured_identity_is_a_value_type()
    {
        theSession.Store(Ranks.Select(rank => new FluentIdentityValueTypeDoc { Key = newId(), Rank = rank }).ToArray());
        await theSession.SaveChangesAsync();

        var ordered = theSession.Query<FluentIdentityValueTypeDoc>().OrderByDescending(x => x.Rank).ThenByDescending(x => x.Key);

        var expected = await ordered.Select(x => x.Key).ToListAsync();
        var paged = await allPages<FluentIdentityValueTypeDoc, GuidId>(
            cursor => ordered.ToJsonPageByCursorAsync(cursor, pageSize: 2), x => x.Key);

        paged.ShouldBe(expected);
    }

    [Fact]
    public async Task pages_through_all_documents_when_the_id_member_is_a_nullable_value_type()
    {
        theSession.Store(Ranks.Select(rank => new NullableValueTypeIdDoc { Id = newId(), Rank = rank }).ToArray());
        await theSession.SaveChangesAsync();

        var ordered = theSession.Query<NullableValueTypeIdDoc>().OrderByDescending(x => x.Rank).ThenByDescending(x => x.Id);

        var expected = await ordered.Select(x => x.Id).ToListAsync();
        var paged = await allPages<NullableValueTypeIdDoc, GuidId?>(
            cursor => ordered.ToJsonPageByCursorAsync(cursor, pageSize: 2), x => x.Id);

        paged.ShouldBe(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task pages_through_every_row_when_a_nullable_value_type_leading_key_holds_nulls(bool descending)
    {
        // Nulls at a page boundary, inside a page, and trailing; ties on the non-null key
        var shared = newId();
        GuidId?[] keys = [null, null, shared, null, shared, newId(), null];
        theSession.Store(keys.Select(key => new NullableValueTypeKeyDoc { Id = Guid.NewGuid(), Key = key }).ToArray());
        await theSession.SaveChangesAsync();

        var ordered = descending
            ? theSession.Query<NullableValueTypeKeyDoc>().OrderByDescending(x => x.Key).ThenByDescending(x => x.Id)
            : theSession.Query<NullableValueTypeKeyDoc>().OrderBy(x => x.Key).ThenBy(x => x.Id);

        var expected = await ordered.Select(x => x.Id).ToListAsync();
        expected.Count.ShouldBe(keys.Length);

        var paged = new List<Guid>();
        string cursor = null;
        do
        {
            var page = await ordered.ToJsonPageByCursorAsync(cursor, pageSize: 2);

            using var items = new MemoryStream(Encoding.UTF8.GetBytes(page.ItemsJson));
            paged.AddRange(theStore.Serializer.FromJson<NullableValueTypeKeyDoc[]>(items).Select(x => x.Id));

            cursor = page.NextCursor;
        } while (cursor != null);

        paged.ShouldBe(expected);
    }

    [Fact]
    public async Task pages_through_all_documents_when_the_value_type_wraps_a_number()
    {
        // A JSON number is what exposes reading the cursor as the wrapper: Newtonsoft can't bind one to it.
        theSession.Store(Ranks.Select((rank, i) => new IntValueTypeIdDoc { Id = IntId.From(i + 1), Rank = rank }).ToArray());
        await theSession.SaveChangesAsync();

        var ordered = theSession.Query<IntValueTypeIdDoc>().OrderByDescending(x => x.Rank).ThenByDescending(x => x.Id);

        var expected = await ordered.Select(x => x.Id).ToListAsync();
        var paged = await allPages<IntValueTypeIdDoc, IntId>(
            cursor => ordered.ToJsonPageByCursorAsync(cursor, pageSize: 2), x => x.Id);

        paged.ShouldBe(expected);
    }

    [Fact]
    public async Task pages_through_all_documents_when_a_leading_key_is_a_duplicated_value_type_field()
    {
        theSession.Store(Ranks.Select(rank => new DuplicatedValueTypeKeyDoc
        {
            Id = Guid.NewGuid(),
            Key = GuidId.From(new Guid(rank, 0, 0, new byte[8]))
        }).ToArray());
        await theSession.SaveChangesAsync();

        var ordered = theSession.Query<DuplicatedValueTypeKeyDoc>().OrderBy(x => x.Key).ThenBy(x => x.Id);

        var expected = await ordered.Select(x => x.Id).ToListAsync();
        var paged = await allPages<DuplicatedValueTypeKeyDoc, Guid>(
            cursor => ordered.ToJsonPageByCursorAsync(cursor, pageSize: 2), x => x.Id);

        paged.ShouldBe(expected);
    }

    [Fact]
    public async Task malformed_cursor_value_throws_argument_exception()
    {
        var tampered = CursorPagination.EncodeCursor(["not-a-guid"], theStore.Serializer);

        await Should.ThrowAsync<ArgumentException>(async () =>
            await theSession.Query<ValueTypeIdDoc>().OrderBy(x => x.Id).ToJsonPageByCursorAsync(tampered, pageSize: 2));
    }

    [Fact]
    public async Task can_use_compare_to_with_a_value_type_in_a_where_clause()
    {
        var ids = Enumerable.Range(1, 3).Select(i => GuidId.From(new Guid(i, 0, 0, new byte[8]))).ToArray();
        theSession.Store(ids.Select(id => new ValueTypeIdDoc { Id = id }).ToArray());
        await theSession.SaveChangesAsync();

        var pivot = ids[1];
        var found = await theSession.Query<ValueTypeIdDoc>().Where(x => x.Id.CompareTo(pivot) < 0).ToListAsync();

        found.Select(x => x.Id).ShouldBe([ids[0]]);

        (await theSession.Query<ValueTypeIdDoc>().Where(x => pivot.CompareTo(x.Id) > 0).CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task can_use_compare_to_with_the_inner_value_of_a_value_type_in_a_where_clause()
    {
        var ids = Enumerable.Range(1, 3).Select(i => new Guid(i, 0, 0, new byte[8])).ToArray();
        theSession.Store(ids.Select((id, i) => new ValueTypeFieldDoc { Id = GuidId.From(id), Name = Description.From($"name{i + 1}") }).ToArray());
        await theSession.SaveChangesAsync();

        object pivotId = ids[1];

        (await theSession.Query<ValueTypeFieldDoc>().Where(x => x.Name.Value.CompareTo("name2") < 0).CountAsync()).ShouldBe(1);
        (await theSession.Query<ValueTypeFieldDoc>().Where(x => x.Name.CompareTo((object)"name2") < 0).CountAsync()).ShouldBe(1);
        (await theSession.Query<ValueTypeFieldDoc>().Where(x => x.Id.CompareTo(pivotId) < 0).CountAsync()).ShouldBe(1);
        (await theSession.Query<ValueTypeFieldDoc>().Where(x => x.Name.Value == "name2").CountAsync()).ShouldBe(1);
    }

    private static GuidId newId() => GuidId.From(Guid.NewGuid());

    private async Task<List<TKey>> allPages<TDoc, TKey>(Func<string, Task<CursorPageResult>> fetch, Func<TDoc, TKey> key)
    {
        var keys = new List<TKey>();
        string cursor = null;
        do
        {
            var page = await fetch(cursor);

            using var items = new MemoryStream(Encoding.UTF8.GetBytes(page.ItemsJson));
            keys.AddRange(theStore.Serializer.FromJson<TDoc[]>(items).Select(key));

            cursor = page.NextCursor;
        } while (cursor != null);

        keys.Count.ShouldBe(Ranks.Length);
        return keys;
    }
}

public class ValueTypeIdDoc
{
    public GuidId Id { get; set; }
    public int Rank { get; set; }
}

public class AttributeIdentityValueTypeDoc
{
    [Identity]
    public GuidId Key { get; set; }

    public int Rank { get; set; }
}

public class FluentIdentityValueTypeDoc
{
    public GuidId Key { get; set; }
    public int Rank { get; set; }
}

public class IntValueTypeIdDoc
{
    public IntId Id { get; set; }
    public int Rank { get; set; }
}

public class DuplicatedValueTypeKeyDoc
{
    public Guid Id { get; set; }
    public GuidId Key { get; set; }
}

public class ValueTypeFieldDoc
{
    public GuidId Id { get; set; }
    public Description Name { get; set; }
}

public class NullableValueTypeIdDoc
{
    public GuidId? Id { get; set; }
    public int Rank { get; set; }
}

public class NullableValueTypeKeyDoc
{
    public Guid Id { get; set; }
    public GuidId? Key { get; set; }
}
