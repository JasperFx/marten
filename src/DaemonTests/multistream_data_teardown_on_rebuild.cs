using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace DaemonTests;

public class Base
{
    public string Id { get; set; }
}

public class ImplementationA: Base
{
}

public class ImplementationB: Base
{
}

public class SomethingHappened
{
    public Guid Id { get; set; }
}

public class ImplementationAProjection: MultiStreamProjection<ImplementationA, string>
{
    public ImplementationAProjection()
    {
        Identity<SomethingHappened>(e => $"a-{e.Id}");
        TeardownDataOnRebuild = false;
    }

    public static ImplementationA Create(
        SomethingHappened e
    ) => new() { Id = $"a-{e.Id}" };
}

public class ImplementationBProjection: MultiStreamProjection<ImplementationB, string>
{
    public ImplementationBProjection()
    {
        Identity<SomethingHappened>(e => $"b-{e.Id}");
        TeardownDataOnRebuild = false;
    }

    public static ImplementationB Create(
        SomethingHappened e
    ) => new() { Id = $"b-{e.Id}" };
}

public class MultiStreamDataTeardownOnRebuildTests: OneOffConfigurationsContext
{
    [Fact]
    public async Task Adheres_to_disabled_teardown_on_rebuild()
    {
        StoreOptions(
            opts =>
            {
                opts.Schema.For<Base>()
                    .AddSubClass<ImplementationA>()
                    .AddSubClass<ImplementationB>();

                opts.Projections.Add<ImplementationAProjection>(ProjectionLifecycle.Inline);

                opts.Projections.Add<ImplementationBProjection>(ProjectionLifecycle.Inline);
            }
        );

        var commonId = Guid.NewGuid();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await using var session = theStore.LightweightSession();

        session.Events.StartStream(
            commonId,
            new SomethingHappened() { Id = commonId }
        );
        await session.SaveChangesAsync();

        var implementationA = await session.LoadAsync<ImplementationA>($"a-{commonId}");
        var implementationB = await session.LoadAsync<ImplementationB>($"b-{commonId}");

        implementationA.ShouldNotBeNull();
        implementationB.ShouldNotBeNull();

        await daemon.RebuildProjectionAsync<ImplementationAProjection>(CancellationToken.None);

        var implementationBAfterRebuildOfA = await session.LoadAsync<ImplementationB>($"b-{commonId}");

        implementationBAfterRebuildOfA.ShouldNotBeNull();
    }
}
