using System.Linq;
using System.Threading.Tasks;
using Alba;
using IssueService.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Marten.AspNetCore.Testing;

public class Bug_2311_honoring_multi_tenancy_in_streaming : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;

    public Bug_2311_honoring_multi_tenancy_in_streaming(AppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task honor_tenancy_in_write_array()
    {
        var store = _fixture.Host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();

        await using var sessionOne = store.LightweightSession("one");
        sessionOne.Store(new Thing{Name = "Thor"});
        sessionOne.Store(new Thing{Name = "Iron Man"});
        sessionOne.Store(new Thing{Name = "Captain America"});
        await sessionOne.SaveChangesAsync();


        await using var sessionTwo = store.LightweightSession("two");
        sessionTwo.Store(new Thing{Name = "Hawkeye"});
        sessionTwo.Store(new Thing{Name = "Black Widow"});
        await sessionTwo.SaveChangesAsync();


        var result = await _fixture.Host.GetAsJson<Thing[]>("/things/one");
        result.Length.ShouldBe(3);

        result.Select(x => x.Name).OrderBy(x => x)
            .ShouldBe(new []{"Captain America", "Iron Man", "Thor"});
    }
}
