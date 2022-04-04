using Microsoft.Extensions.DependencyInjection;
using Oakton;
using SharedDataLayer;

namespace Marten.Testing.Harness;

public class TestCommand : OaktonAsyncCommand<NetCoreInput>
{
    public override async Task<bool> Execute(NetCoreInput input)
    {
        using var host = input.BuildHost();

        var store = host.Services.GetRequiredService<IDocumentStore>();
        using var session = store.LightweightSession();

        session.Store(new Invoice());

        await session.SaveChangesAsync();

        return true;
    }
}
