using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Pagination;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

// #4954 binds non-string Select projection constants as command parameters. These pin that the
// parameter is bound in every statement shape that carries the projection, paging included.
public class select_projection_constants_with_paging: BugIntegrationContext
{
    private async Task<Guid> storeTwo()
    {
        var runId = Guid.NewGuid();
        theSession.Store(
            new ProjectionConstantDoc { Id = Guid.NewGuid(), RunId = runId, Name = "first", Published = true },
            new ProjectionConstantDoc { Id = Guid.NewGuid(), RunId = runId, Name = "second", Published = true });
        await theSession.SaveChangesAsync();
        return runId;
    }

    [Fact]
    public async Task literal_constants_in_a_record_constructor()
    {
        var runId = await storeTwo();

        var results = await theSession.Query<ProjectionConstantDoc>()
            .Where(x => x.RunId == runId)
            .OrderBy(x => x.Name)
            .Select(x => new ProjectionConstantOverview(x.Id, x.Name, x.Published, false, false))
            .ToListAsync();

        results.Select(x => x.Name).ShouldBe(["first", "second"]);
        results.ShouldAllBe(x => !x.HasAvailableSample && !x.IsOverdue);
    }

    [Fact]
    public async Task literal_constants_in_a_paged_projection()
    {
        var runId = await storeTwo();

        var page = await theSession.Query<ProjectionConstantDoc>()
            .Where(x => x.RunId == runId)
            .OrderBy(x => x.Name)
            .Select(x => new ProjectionConstantOverview(x.Id, x.Name, x.Published, false, false))
            .ToPagedListAsync(1, 1);

        page.TotalItemCount.ShouldBe(2);
        page.Single().Name.ShouldBe("first");
        page.Single().IsOverdue.ShouldBeFalse();
    }

    [Fact]
    public async Task captured_constants_in_a_paged_projection()
    {
        var runId = await storeTwo();
        var overdue = true;

        var page = await theSession.Query<ProjectionConstantDoc>()
            .Where(x => x.RunId == runId)
            .OrderBy(x => x.Name)
            .Select(x => new ProjectionConstantOverview(x.Id, x.Name, x.Published, false, overdue))
            .ToPagedListAsync(2, 1);

        page.TotalItemCount.ShouldBe(2);
        page.Single().Name.ShouldBe("second");
        page.Single().IsOverdue.ShouldBeTrue();
    }
}

public class ProjectionConstantDoc
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string Name { get; set; } = "";
    public bool Published { get; set; }
}

public record ProjectionConstantOverview(Guid Id, string Name, bool Published, bool HasAvailableSample, bool IsOverdue);
