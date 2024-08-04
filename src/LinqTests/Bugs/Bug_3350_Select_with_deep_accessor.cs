using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_3350_Select_with_deep_accessor : BugIntegrationContext
{
    [Fact]
    public async Task get_the_deeply_nested_value_in_select()
    {
        await theStore.BulkInsertAsync(Target.GenerateRandomData(1000).ToArray());

        var views = await theSession.Query<Target>()
            .Where(x => x.Inner != null && x.Inner.String != null)
            .Select(x => new SelectedView { Number = x.Number, Text = x.Inner.String }).ToListAsync();

        views.ShouldNotBeEmpty();

        foreach (var view in views)
        {
            view.Text.ShouldNotBeNullOrEmpty();
        }

    }
}

public class SelectedView
{
    public string Text { get; set; }
    public int Number { get; set; }
}
