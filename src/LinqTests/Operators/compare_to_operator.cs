using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

/// <summary>
///     Retrofit coverage for #4920, which generalized CompareTo() translation from
///     string-only to any instance CompareTo(other): int — exercised here across the
///     primitive types Marten stores
/// </summary>
public class compare_to_operator: IntegrationContext
{
    private Target[] _targets;

    public compare_to_operator(DefaultStoreFixture fixture): base(fixture)
    {
    }

    private async Task seed()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));

        _targets = Enumerable.Range(1, 20).Select(i => new Target
        {
            Id = Guid.NewGuid(),
            Number = i,
            Long = i * 1000L,
            Double = i * 1.5,
            Decimal = i * 2.5m,
            Float = i * 0.5f,
            String = $"name-{i:D2}",
            Date = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddDays(i),
            DateOffset = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i),
            Color = (Colors)(i % 3)
        }).ToArray();

        theSession.Store(_targets);
        await theSession.SaveChangesAsync();
    }

    private async Task assertCount(System.Linq.Expressions.Expression<Func<Target, bool>> filter, int expected)
    {
        expected.ShouldBeGreaterThan(0);
        var actual = await theSession.Query<Target>().Where(filter).CountAsync();
        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task int_compare_to()
    {
        await seed();
        await assertCount(x => x.Number.CompareTo(10) > 0, _targets.Count(x => x.Number.CompareTo(10) > 0));
        await assertCount(x => x.Number.CompareTo(10) <= 0, _targets.Count(x => x.Number.CompareTo(10) <= 0));
        await assertCount(x => x.Number.CompareTo(10) == 0, _targets.Count(x => x.Number.CompareTo(10) == 0));
    }

    [Fact]
    public async Task long_compare_to()
    {
        await seed();
        await assertCount(x => x.Long.CompareTo(7000L) >= 0, _targets.Count(x => x.Long.CompareTo(7000L) >= 0));
    }

    [Fact]
    public async Task double_compare_to()
    {
        await seed();
        await assertCount(x => x.Double.CompareTo(15.0) < 0, _targets.Count(x => x.Double.CompareTo(15.0) < 0));
    }

    [Fact]
    public async Task decimal_compare_to()
    {
        await seed();
        await assertCount(x => x.Decimal.CompareTo(25.0m) > 0, _targets.Count(x => x.Decimal.CompareTo(25.0m) > 0));
    }

    [Fact]
    public async Task float_compare_to()
    {
        await seed();
        await assertCount(x => x.Float.CompareTo(5.0f) < 0, _targets.Count(x => x.Float.CompareTo(5.0f) < 0));
    }

    [Fact]
    public async Task datetime_compare_to()
    {
        await seed();
        var pivot = new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Unspecified);
        await assertCount(x => x.Date.CompareTo(pivot) > 0, _targets.Count(x => x.Date.CompareTo(pivot) > 0));
    }

    [Fact]
    public async Task datetimeoffset_compare_to()
    {
        await seed();
        var pivot = new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero);
        await assertCount(x => x.DateOffset.CompareTo(pivot) <= 0,
            _targets.Count(x => x.DateOffset.CompareTo(pivot) <= 0));
    }

    [Fact]
    public async Task string_compare_to_still_works()
    {
        await seed();
        await assertCount(x => x.String.CompareTo("name-10") > 0,
            _targets.Count(x => string.Compare(x.String, "name-10", StringComparison.Ordinal) > 0));
    }

    [Fact]
    public async Task constant_receiver_compare_to_member()
    {
        await seed();
        // the comparison can sit on either side
        await assertCount(x => 10.CompareTo(x.Number) > 0, _targets.Count(x => 10.CompareTo(x.Number) > 0));
    }

    [Fact]
    public async Task compare_to_against_non_zero_throws()
    {
        await seed();
        await Should.ThrowAsync<BadLinqExpressionException>(async () =>
        {
            await theSession.Query<Target>().Where(x => x.Number.CompareTo(10) > 1).ToListAsync();
        });
    }
}
