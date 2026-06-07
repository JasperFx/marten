using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

// Projecting a DateOnly/TimeOnly member directly -- Query<T>().Select(x => x.SomeDateOnly)
// -- threw System.Text.Json "'-' is an invalid end of a number": the bare scalar projection
// fell through to DataSelectClause, which JSON-deserializes the quote-stripped `data ->> 'x'`
// text. Fixed by routing DateOnly/TimeOnly through NewScalarSelectClause (native date/time read).
public class Bug_dateonly_timeonly_scalar_projection: BugIntegrationContext
{
    public sealed class DocWithDateOnly
    {
        public Guid Id { get; set; }
        public DateOnly Date { get; set; }
        public TimeOnly Time { get; set; }
        public DateOnly? NullableDate { get; set; }
        public TimeOnly? NullableTime { get; set; }
    }

    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization();
            opts.Schema.For<DocWithDateOnly>();
        });
    }

    private async Task seedAsync()
    {
        await using var session = theStore.LightweightSession();
        session.Store(
            new DocWithDateOnly
            {
                Id = Guid.NewGuid(),
                Date = new DateOnly(2026, 4, 29),
                Time = new TimeOnly(9, 30, 0),
                NullableDate = new DateOnly(2026, 4, 29),
                NullableTime = new TimeOnly(9, 30, 0)
            },
            new DocWithDateOnly
            {
                Id = Guid.NewGuid(),
                Date = new DateOnly(2026, 5, 1),
                Time = new TimeOnly(14, 0, 0),
                NullableDate = null,
                NullableTime = null
            });
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task select_dateonly_scalar_directly()
    {
        ConfigureStore();
        await seedAsync();

        await using var query = theStore.QuerySession();
        var dates = await query.Query<DocWithDateOnly>()
            .OrderByDescending(x => x.Date)
            .Select(x => x.Date)
            .ToListAsync();

        dates.ShouldBe(new[] { new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 29) });
    }

    [Fact]
    public async Task select_timeonly_scalar_directly()
    {
        ConfigureStore();
        await seedAsync();

        await using var query = theStore.QuerySession();
        var times = await query.Query<DocWithDateOnly>()
            .OrderBy(x => x.Date)
            .Select(x => x.Time)
            .ToListAsync();

        times.ShouldBe(new[] { new TimeOnly(9, 30, 0), new TimeOnly(14, 0, 0) });
    }

    [Fact]
    public async Task select_nullable_dateonly_scalar_directly()
    {
        ConfigureStore();
        await seedAsync();

        await using var query = theStore.QuerySession();
        var dates = await query.Query<DocWithDateOnly>()
            .OrderBy(x => x.Date)
            .Select(x => x.NullableDate)
            .ToListAsync();

        dates.Count.ShouldBe(2);
        dates[0].ShouldBe(new DateOnly(2026, 4, 29));
        dates[1].ShouldBeNull();
    }

    [Fact]
    public async Task select_nullable_timeonly_scalar_directly()
    {
        ConfigureStore();
        await seedAsync();

        await using var query = theStore.QuerySession();
        var times = await query.Query<DocWithDateOnly>()
            .OrderBy(x => x.Date)
            .Select(x => x.NullableTime)
            .ToListAsync();

        times.Count.ShouldBe(2);
        times[0].ShouldBe(new TimeOnly(9, 30, 0));
        times[1].ShouldBeNull();
    }

    [Fact]
    public async Task select_dateonly_with_where()
    {
        ConfigureStore();
        await seedAsync();

        await using var query = theStore.QuerySession();
        var dates = await query.Query<DocWithDateOnly>()
            .Where(x => x.Date >= new DateOnly(2026, 5, 1))
            .Select(x => x.Date)
            .ToListAsync();

        dates.ShouldBe(new[] { new DateOnly(2026, 5, 1) });
    }
}
