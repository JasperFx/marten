using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;

namespace LinqTests.Bugs;

public class EnumArrayDoc
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public FlatColour[] Colours { get; set; } = [];
    public List<FlatColour> ColourList { get; set; } = new();
}

// #5940: SelectMany() over an enum array read each flattened element through Npgsql's typed reader as the enum
// type, which cannot read the integer or text column the enum is stored as, and threw InvalidCastException.
// Every shape runs with both enum storages and is compared with LINQ to Objects.
public class Bug_5940_select_many_over_enum_array: OneOffConfigurationsContext
{
    private static readonly int[] StorageOrder = [3, 8, 1, 6, 4, 7, 2, 5];

    private static readonly EnumArrayDoc[] Docs = StorageOrder.Select(i => new EnumArrayDoc
    {
        Id = Guid.NewGuid(),
        Number = i,
        Colours = Enumerable.Range(0, i % 4).Select(v => (FlatColour)((i + v) % 3)).ToArray(),
        ColourList = Enumerable.Range(0, i % 3).Select(v => (FlatColour)((i * 2 + v) % 3)).ToList()
    }).ToArray();

    private async Task<IQuerySession> seeded(EnumStorage storage)
    {
        StoreOptions(opts => opts.UseSystemTextJsonForSerialization(storage));

        foreach (var doc in Docs)
        {
            theSession.Store(doc);
            await theSession.SaveChangesAsync();
        }

        return theStore.QuerySession();
    }

    public static IEnumerable<object[]> Cases() =>
        from storage in new[] { EnumStorage.AsInteger, EnumStorage.AsString }
        from shape in new[]
        {
            "fetch", "unordered", "list", "where", "where_equals", "order_by", "order_by_descending", "distinct",
            "take_before", "skip_take_after", "where_then_take", "nothing"
        }
        select new object[] { storage, shape };

    /// <summary>
    /// PostgreSQL orders an enum the way it is stored: by its integer value, or by its name when it is stored as
    /// a string. That is the same ordering as OrderBy() over an enum member of a document.
    /// </summary>
    private static IOrderedEnumerable<FlatColour> storedOrder(IEnumerable<FlatColour> colours, EnumStorage storage,
        bool descending)
    {
        if (storage == EnumStorage.AsInteger)
        {
            return descending ? colours.OrderByDescending(c => c) : colours.OrderBy(c => c);
        }

        return descending
            ? colours.OrderByDescending(c => c.ToString(), StringComparer.Ordinal)
            : colours.OrderBy(c => c.ToString(), StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task matches_linq_to_objects(EnumStorage storage, string shape)
    {
        await using var session = await seeded(storage);
        var q = session.Query<EnumArrayDoc>();
        var ordered = Docs.OrderBy(x => x.Number).ToArray();

        var (query, expected, inOrder) = shape switch
        {
            "fetch" => (q.OrderBy(x => x.Number).SelectMany(x => x.Colours),
                ordered.SelectMany(x => x.Colours).ToList(), true),
            "unordered" => (q.SelectMany(x => x.Colours), Docs.SelectMany(x => x.Colours).ToList(), false),
            "list" => (q.OrderBy(x => x.Number).SelectMany(x => x.ColourList),
                ordered.SelectMany(x => x.ColourList).ToList(), true),
            "where" => (q.OrderBy(x => x.Number).SelectMany(x => x.Colours).Where(c => c != FlatColour.Red),
                ordered.SelectMany(x => x.Colours).Where(c => c != FlatColour.Red).ToList(), true),
            "where_equals" => (q.OrderBy(x => x.Number).SelectMany(x => x.ColourList).Where(c => c == FlatColour.Blue),
                ordered.SelectMany(x => x.ColourList).Where(c => c == FlatColour.Blue).ToList(), true),
            "order_by" => (q.SelectMany(x => x.Colours).OrderBy(c => c),
                storedOrder(Docs.SelectMany(x => x.Colours), storage, false).ToList(), true),
            "order_by_descending" => (q.SelectMany(x => x.Colours).OrderByDescending(c => c),
                storedOrder(Docs.SelectMany(x => x.Colours), storage, true).ToList(), true),
            "distinct" => (q.SelectMany(x => x.Colours).Distinct(),
                Docs.SelectMany(x => x.Colours).Distinct().ToList(), false),
            "take_before" => (q.OrderBy(x => x.Number).Skip(2).Take(3).SelectMany(x => x.Colours),
                ordered.Skip(2).Take(3).SelectMany(x => x.Colours).ToList(), true),
            "skip_take_after" => (q.OrderBy(x => x.Number).SelectMany(x => x.Colours).Skip(2).Take(4),
                ordered.SelectMany(x => x.Colours).Skip(2).Take(4).ToList(), true),
            "where_then_take" => (q.OrderBy(x => x.Number).SelectMany(x => x.Colours).Where(c => c != FlatColour.Green).Take(3),
                ordered.SelectMany(x => x.Colours).Where(c => c != FlatColour.Green).Take(3).ToList(), true),
            "nothing" => (q.Where(x => x.Number > 100).SelectMany(x => x.Colours), new List<FlatColour>(), true),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        var sql = query.ToCommand().CommandText;
        var actual = await query.ToListAsync();

        if (inOrder)
        {
            actual.ShouldBe(expected, sql);
        }
        else
        {
            actual.ShouldBe(expected, ignoreOrder: true, customMessage: sql);
        }

        (await query.CountAsync()).ShouldBe(expected.Count, sql);
        (await query.LongCountAsync()).ShouldBe(expected.LongCount(), sql);
        (await query.AnyAsync()).ShouldBe(expected.Any(), sql);

        if (!inOrder) return;

        (await query.FirstOrDefaultAsync()).ShouldBe(expected.FirstOrDefault(), sql);
        if (expected.Any())
        {
            (await query.FirstAsync()).ShouldBe(expected.First(), sql);
        }
        else
        {
            await Should.ThrowAsync<InvalidOperationException>(() => query.FirstAsync());
        }
    }

    [Theory]
    [InlineData(EnumStorage.AsInteger)]
    [InlineData(EnumStorage.AsString)]
    public async Task single_after_select_many(EnumStorage storage)
    {
        await using var session = await seeded(storage);

        var expected = Docs.Single(x => x.Number == 5).Colours.Single();
        (await session.Query<EnumArrayDoc>().Where(x => x.Number == 5).SelectMany(x => x.Colours).SingleAsync())
            .ShouldBe(expected);
    }

    [Fact]
    public async Task select_the_integer_value_when_stored_as_integers()
    {
        await using var session = await seeded(EnumStorage.AsInteger);

        (await session.Query<EnumArrayDoc>().OrderBy(x => x.Number).SelectMany(x => x.Colours)
                .Select(c => (int)c * 10).Where(v => v > 0).ToListAsync())
            .ShouldBe(Docs.OrderBy(x => x.Number).SelectMany(x => x.Colours).Select(c => (int)c * 10).Where(v => v > 0));
    }

    // Under EnumStorage.AsString the stored value is the name, so (int)c has no SQL translation
    [Fact]
    public async Task the_integer_value_is_refused_when_stored_as_strings()
    {
        await using var session = await seeded(EnumStorage.AsString);

        await Should.ThrowAsync<BadLinqExpressionException>(() =>
            session.Query<EnumArrayDoc>().SelectMany(x => x.Colours).Select(c => (int)c).ToListAsync());
    }

    // SQL for every SelectMany() shape is unchanged by #5940; only how the rows are read back changed
    [Theory]
    [InlineData(EnumStorage.AsInteger, "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Colours' as jsonb)) as data from {0}.mt_doc_enumarraydoc as d) select CAST(data as integer) as data from mt_temp_id_list1CTE as d;")]
    [InlineData(EnumStorage.AsString, "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Colours' as jsonb)) as data from {0}.mt_doc_enumarraydoc as d) select CAST(data as varchar) as data from mt_temp_id_list1CTE as d;")]
    public async Task sql_is_unchanged(EnumStorage storage, string expected)
    {
        await using var session = await seeded(storage);
        var recorder = new ExecutedCommandRecorder();
        session.Logger = recorder;

        await session.Query<EnumArrayDoc>().SelectMany(x => x.Colours).ToListAsync();

        recorder.Commands.Single().Sql.ShouldBe(string.Format(expected, SchemaName));
    }
}
