using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
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

public class Base2
{
    public string Id { get; set; }
}

public class ImplementationA2: Base2
{
}

public class ImplementationB2: Base2
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
        Options.TeardownDataOnRebuild = false;
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
        Options.TeardownDataOnRebuild = false;
    }

    public static ImplementationB Create(
        SomethingHappened e
    ) => new() { Id = $"b-{e.Id}" };
}

public class ImplementationA2Projection: MultiStreamProjection<ImplementationA2, string>
{
    public ImplementationA2Projection()
    {
        Identity<SomethingHappened>(e => $"a2-{e.Id}");
    }

    public static ImplementationA2 Create(
        SomethingHappened e
    ) => new() { Id = $"a2-{e.Id}" };
}

public class ImplementationB2Projection: MultiStreamProjection<ImplementationB2, string>
{
    public ImplementationB2Projection()
    {
        Identity<SomethingHappened>(e => $"b2-{e.Id}");
    }

    public static ImplementationB2 Create(
        SomethingHappened e
    ) => new() { Id = $"b2-{e.Id}" };
}

public class MultiStreamDataTeardownOnRebuildTests
{
    public class When_teardown_is_set_to_false_in_projection: OneOffConfigurationsContext
    {
        [Fact]
        public async Task Does_not_teardown_on_rebuild()
        {
            StoreOptions(
                opts =>
                {
                    opts.Schema.For<Base>()
                        .AddSubClass<ImplementationA>()
                        .AddSubClass<ImplementationB>();

                    opts.Projections.Add<ImplementationAProjection>(
                        ProjectionLifecycle.Inline
                    );
                    opts.Projections.Add<ImplementationBProjection>(
                        ProjectionLifecycle.Inline
                    );
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

    public class When_teardown_is_set_to_false_in_registration_options: OneOffConfigurationsContext
    {
        [Fact]
        public async Task Does_not_teardown_on_rebuild()
        {
            StoreOptions(
                opts =>
                {
                    opts.Schema.For<Base2>()
                        .AddSubClass<ImplementationA2>()
                        .AddSubClass<ImplementationB2>();

                    opts.Projections.Add<ImplementationA2Projection>(
                        ProjectionLifecycle.Inline,
                        options => options.TeardownDataOnRebuild = false
                    );
                    opts.Projections.Add<ImplementationB2Projection>(
                        ProjectionLifecycle.Inline,
                        options => options.TeardownDataOnRebuild = false
                    );
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

            var implementationA = await session.LoadAsync<ImplementationA2>($"a2-{commonId}");
            var implementationB = await session.LoadAsync<ImplementationB2>($"b2-{commonId}");

            implementationA.ShouldNotBeNull();
            implementationB.ShouldNotBeNull();

            await daemon.RebuildProjectionAsync<ImplementationA2Projection>(CancellationToken.None);

            var implementationBAfterRebuildOfA = await session.LoadAsync<ImplementationB2>($"b2-{commonId}");

            implementationBAfterRebuildOfA.ShouldNotBeNull();
        }
    }
}
