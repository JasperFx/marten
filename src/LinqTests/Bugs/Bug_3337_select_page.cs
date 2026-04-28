using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Pagination;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
namespace LinqTests.Bugs;

public class Bug_3337_select_page : BugIntegrationContext
{
    [Fact]
    public async Task try_it_out()
    {
        // Reset the shared Target random sequence so the data we generate is
        // deterministic regardless of sibling-test order. Without this, a
        // surprise zero in `Number` or null in `String` would silently flake.
        Target.ResetRandomSeed();
        await theStore.BulkInsertAsync(Target.GenerateRandomData(1000).ToArray());

        var results = await theSession.Query<Target>().Where(x => x.Inner != null)
            .Select(x => new SelectedGuy() { Id = x.Id, Number = x.Inner.Number, Text = x.String })
            .Take(5).Stats(out var statistics).ToListAsync();
            //.ToPagedListAsync(1, 5);

        foreach (var result in results)
        {
            result.Number.ShouldNotBe(0);
            result.Id.ShouldNotBe(Guid.Empty);
            result.Text.ShouldNotBeNull();
        }
    }
}

public class SelectedGuy
{
    public int Number { get; set; }
    public Guid Id { get; set; }
    public string Text { get; set; }
}
