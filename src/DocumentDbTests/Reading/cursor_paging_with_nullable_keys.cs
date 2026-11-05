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
using Xunit;

namespace DocumentDbTests.Reading;

public class NullableKeyDoc
{
    public Guid Id { get; set; }
    public int? Rank { get; set; }
    public DateTime? Stamp { get; set; }
    public string Name { get; set; }
}

public class cursor_paging_with_nullable_keys: OneOffConfigurationsContext
{
    public static IEnumerable<object[]> Layouts()
    {
        // Each layout is the leading key per row, null where the row holds no value.
        var layouts = new int?[][]
        {
            [null, null, 1, 2, 3], // nulls first, landing on a page boundary
            [1, 2, null, 3, 4], // null inside a page
            [1, 2, 3, null, null], // nulls last
            [1, null, 2, null, 3, null], // interleaved
            [null, null, null, null, null], // all null
            [1, 1, null, null, 2, 2], // duplicates of both null and non-null
            [null, 5, 5, null, null, 5, 5] // ties spanning page boundaries
        };

        foreach (var layout in layouts)
        {
            foreach (var descending in new[] { false, true })
            {
                yield return [layout, descending];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public async Task pages_through_every_row_with_a_nullable_int_leading_key(int?[] ranks, bool descending)
    {
        await store(ranks);

        var ordered = descending
            ? theSession.Query<NullableKeyDoc>().OrderByDescending(x => x.Rank).ThenByDescending(x => x.Id)
            : theSession.Query<NullableKeyDoc>().OrderBy(x => x.Rank).ThenBy(x => x.Id);

        await assertSamePaging(ordered, ranks.Length);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public async Task pages_through_every_row_with_a_nullable_date_leading_key(int?[] ranks, bool descending)
    {
        await store(ranks);

        var ordered = descending
            ? theSession.Query<NullableKeyDoc>().OrderByDescending(x => x.Stamp).ThenByDescending(x => x.Id)
            : theSession.Query<NullableKeyDoc>().OrderBy(x => x.Stamp).ThenBy(x => x.Id);

        await assertSamePaging(ordered, ranks.Length);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public async Task pages_through_every_row_with_a_nullable_string_leading_key(int?[] ranks, bool descending)
    {
        await store(ranks);

        var ordered = descending
            ? theSession.Query<NullableKeyDoc>().OrderByDescending(x => x.Name).ThenByDescending(x => x.Id)
            : theSession.Query<NullableKeyDoc>().OrderBy(x => x.Name).ThenBy(x => x.Id);

        await assertSamePaging(ordered, ranks.Length);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public async Task pages_through_every_row_with_two_nullable_keys(int?[] ranks, bool descending)
    {
        await store(ranks);

        var ordered = descending
            ? theSession.Query<NullableKeyDoc>().OrderByDescending(x => x.Rank).ThenBy(x => x.Stamp).ThenByDescending(x => x.Id)
            : theSession.Query<NullableKeyDoc>().OrderBy(x => x.Rank).ThenByDescending(x => x.Stamp).ThenBy(x => x.Id);

        await assertSamePaging(ordered, ranks.Length);
    }

    private async Task store(int?[] ranks)
    {
        var docs = ranks.Select((rank, i) => new NullableKeyDoc
        {
            Id = Guid.NewGuid(),
            Rank = rank,
            // Null rank and null stamp/name together, but with a second null pattern for the trailing key
            Stamp = rank.HasValue ? new DateTime(2026, 1, rank.Value, 0, 0, 0, DateTimeKind.Utc) : null,
            Name = rank.HasValue ? $"n{rank.Value}" : null
        }).ToArray();

        theSession.Store(docs);
        await theSession.SaveChangesAsync();
    }

    private async Task assertSamePaging(IQueryable<NullableKeyDoc> ordered, int total)
    {
        var expected = await ordered.Select(x => x.Id).ToListAsync();
        expected.Count.ShouldBe(total);

        var paged = new List<Guid>();
        string cursor = null;
        do
        {
            var page = await ordered.ToJsonPageByCursorAsync(cursor, pageSize: 2);

            using var items = new MemoryStream(Encoding.UTF8.GetBytes(page.ItemsJson));
            paged.AddRange(theStore.Serializer.FromJson<NullableKeyDoc[]>(items).Select(x => x.Id));

            cursor = page.NextCursor;
        } while (cursor != null);

        paged.ShouldBe(expected);
    }
}
