using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace EventStoreMigrations;

public class TestCommand : JasperFxAsyncCommand<NetCoreInput>
{
    public override async Task<bool> Execute(NetCoreInput input)
    {
        using var host = input.BuildHost();
        var store = host.Services.GetRequiredService<IDocumentStore>();
        await using var session = store.LightweightSession();
        session.Events.StartStream(Guid.NewGuid().ToString(), new Started());
        await session.SaveChangesAsync();

        return true;
    }
}
