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

    [Fact]
    public async Task a_missing_key_projected_into_a_non_nullable_target_is_still_stripped()
    {
        // The other direction of the same mismatch, and the case #5461 was filed for: the source
        // member is nullable but the TARGET is not, so an absent key has to stay absent or the
        // projection cannot be deserialized at all.
        theSession.Store(new Period5499
        {
            Id = Guid.NewGuid(), Name = "unwrapped", StartDate = new DateOnly(2026, 1, 1)
        });
        await theSession.SaveChangesAsync();

        var result = await theSession.Query<Period5499>()
            .Where(x => x.Name == "unwrapped")
            .Select(x => new CountOnly5499 { Name = x.Name, Count = x.Count!.Value })
            .FirstOrDefaultAsync();

        result.ShouldNotBeNull();
        result.Count.ShouldBe(0);
    }

    [Fact]
    public void an_anonymous_type_reads_its_constructor_parameter()
    {
        // An anonymous type has no member bindings -- its members ARE the constructor parameters,
        // so that is where the declared types have to be read from.
        var sql = theSession.Query<Period5499>()
            .Select(x => new { x.Name, x.StartDate, x.EndDate, x.Count })
            .ToCommand().CommandText;

        var stripped = sql.Substring(sql.IndexOf("jsonb_strip_nulls", StringComparison.Ordinal));
        stripped.ShouldContain("'StartDate'");
        stripped.ShouldNotContain("'EndDate'");
        stripped.ShouldNotContain("'Count'");
    }
}

/// <summary>
/// The duplicated-field shape needs its own schema, because duplicating a member rewrites the
/// document table -- so it does not share the `bugs` schema with the tests above.
/// </summary>
public class Bug_5499_duplicated_nullable_member_in_a_select_projection: OneOffConfigurationsContext
{
    [Fact]
    public async Task a_duplicated_nullable_member_keeps_its_null_too()
    {
        // A DuplicatedField is an IQueryableMember but NOT a QueryableMember, and it delegates
        // MemberType to the member underneath -- already unwrapped from Nullable<T>. Reading the
        // source member's declared type never reaches it; reading the target's does.
        StoreOptions(opts => opts.Schema.For<DuplicatedPeriod5499>().Duplicate(x => x.EndDate));

        theSession.Store(new DuplicatedPeriod5499
        {
            Id = Guid.NewGuid(), Name = "duplicated", StartDate = new DateOnly(2026, 1, 1)
        });
        await theSession.SaveChangesAsync();

        var json = await theSession.Query<DuplicatedPeriod5499>()
            .Where(x => x.Name == "duplicated")
            .Select(x => new DuplicatedProjection5499
            {
                Name = x.Name, StartDate = x.StartDate, EndDate = x.EndDate
            })
            .ToJsonArray();

        json.ShouldContain("\"EndDate\": null");

        var result = await theSession.Query<DuplicatedPeriod5499>()
            .Where(x => x.Name == "duplicated")
            .Select(x => new DuplicatedProjection5499
            {
                Name = x.Name, StartDate = x.StartDate, EndDate = x.EndDate
            })
            .FirstOrDefaultAsync();

        result.ShouldNotBeNull();
        result.EndDate.ShouldBeNull();
    }
}

public class DuplicatedPeriod5499
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}

public sealed record DuplicatedProjection5499
{
    public required string Name { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly? EndDate { get; init; }
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

/// <summary>
/// Deliberately NOT `required`: the key this projection reads is absent from the stored document,
/// and stripping it is the whole point -- a `required` non-nullable value type here is unsatisfiable
/// in either direction, because leaving the key in means deserializing an explicit null into an int.
/// </summary>
public sealed record CountOnly5499
{
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed record ProjectedPeriod5499
{
    public required string Name { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly? EndDate { get; init; }
    public required int? Count { get; init; }
    public required DateTimeOffset? StartedAt { get; init; }
}
