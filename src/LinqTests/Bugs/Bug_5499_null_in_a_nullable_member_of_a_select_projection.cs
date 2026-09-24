#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

public class Bug_5499_null_in_a_nullable_member_of_a_select_projection: BugIntegrationContext
{
    [Fact]
    public async Task a_stored_null_in_a_nullable_value_type_stays_in_the_projection()
    {
        theSession.Store(new Period5499 { Id = Guid.NewGuid(), Name = "open", StartDate = new DateOnly(2026, 1, 1) });
        await theSession.SaveChangesAsync();

        var result = await theSession.Query<Period5499>()
            .Where(x => x.Name == "open")
            .Select(x => new ProjectedPeriod5499
            {
                Name = x.Name,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                Count = x.Count,
                StartedAt = x.StartedAt
            })
            .FirstOrDefaultAsync();

        result.ShouldNotBeNull();
        result.StartDate.ShouldBe(new DateOnly(2026, 1, 1));
        result.EndDate.ShouldBeNull();
        result.Count.ShouldBeNull();
        result.StartedAt.ShouldBeNull();
    }

    [Fact]
    public async Task the_projected_json_keeps_the_null_keys()
    {
        theSession.Store(new Period5499 { Id = Guid.NewGuid(), Name = "json", StartDate = new DateOnly(2026, 1, 1) });
        await theSession.SaveChangesAsync();

        // A serializer that enforces `required` refuses an absent key, so the key must be there.
        var json = await theSession.Query<Period5499>()
            .Where(x => x.Name == "json")
            .Select(x => new ProjectedPeriod5499
            {
                Name = x.Name,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                Count = x.Count,
                StartedAt = x.StartedAt
            })
            .ToJsonArray();

        json.ShouldContain("\"EndDate\": null");
        json.ShouldContain("\"Count\": null");
        json.ShouldContain("\"StartedAt\": null");
    }

    [Fact]
    public void only_the_non_nullable_scalar_goes_through_jsonb_strip_nulls()
    {
        var sql = theSession.Query<Period5499>()
            .Select(x => new ProjectedPeriod5499
            {
                Name = x.Name,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                Count = x.Count,
                StartedAt = x.StartedAt
            })
            .ToCommand().CommandText;

        sql.ShouldContain("jsonb_strip_nulls(jsonb_build_object('StartDate'");

        var stripped = sql.Substring(sql.IndexOf("jsonb_strip_nulls", StringComparison.Ordinal));
        stripped.ShouldNotContain("'EndDate'");
        stripped.ShouldNotContain("'Count'");
        stripped.ShouldNotContain("'StartedAt'");
    }
}

public class Period5499
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? Count { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
}

public sealed record ProjectedPeriod5499
{
    public required string Name { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly? EndDate { get; init; }
    public required int? Count { get; init; }
    public required DateTimeOffset? StartedAt { get; init; }
}
