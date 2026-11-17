using System;
using System.Linq;
using System.Threading.Tasks;
using Bobcat.CritterStack;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten;
using Shouldly;
using Xunit;

namespace DaemonTests.Specifications;

/// <summary>
/// A projection wait that times out is reported as Bobcat's <see cref="ProjectionCatchUpTimeoutException" />,
/// naming each shard that was behind and where it stood, rather than a bare TimeoutException. Each test forces the
/// timeout on purpose by never running a daemon, and pins what the diagnosis says.
/// </summary>
/// <remarks>
/// Deliberately not a <c>[BobcatFeature]</c>: these tests expect the error a recorded specification would report.
/// </remarks>
public class catch_up_timeout_diagnostics: IClassFixture<ShipmentsHostWithNoDaemon>
{
    private readonly ShipmentsHostWithNoDaemon _app;
    private readonly ITestOutputHelper _output;

    public catch_up_timeout_diagnostics(ShipmentsHostWithNoDaemon app, ITestOutputHelper output)
    {
        _app = app;
        _output = output;
    }

    [Fact]
    public async Task a_read_after_a_daemon_that_never_ran_names_the_shard_behind()
    {
        var scenario = new CritterStackScenario(_app.Host) { ProjectionTimeout = 2.Seconds() };

        var id = Guid.NewGuid();
        await scenario.GivenEvents<ShipmentSummary>(id, new ShipmentPacked(id, 1));

        var timeout = await Should.ThrowAsync<ProjectionCatchUpTimeoutException>(
            () => scenario.ThenReadModel<ShipmentSummary>(id));

        _output.WriteLine(timeout.Message);

        timeout.ReadType.ShouldBe(typeof(ShipmentSummary));
        timeout.Identity.ShouldBe(id);
        timeout.Behind.ShouldContain(x => x.StartsWith("'ShipmentSummary:All' at 0 of "));
    }
}

/// <summary>The same diagnosis through <see cref="DaemonContext" />'s own waits.</summary>
public class daemon_context_catch_up_timeout_diagnostics: DaemonContext
{
    public daemon_context_catch_up_timeout_diagnostics(ITestOutputHelper output): base(output)
    {
    }

    [Fact]
    public async Task waiting_for_catch_up_without_a_daemon_names_the_shard_behind()
    {
        NumberOfStreams = 3;
        StoreOptions(x => x.Projections.Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Async));
        await PublishSingleThreaded();

        var timeout = await Should.ThrowAsync<ProjectionCatchUpTimeoutException>(
            () => WaitForCatchUp(typeof(Trip), 2.Seconds()));

        _output.WriteLine(timeout.Message);

        timeout.Wait.ShouldBe("the Trip read model has caught up");
        // "of N" is the store's highest sequence, which a cleaned store does not reset to NumberOfEvents
        timeout.Behind.ShouldHaveSingleItem().ShouldStartWith("'TripCustomName:All' at 0 of ");
    }

    [Fact]
    public async Task waiting_on_a_shard_that_never_starts_names_where_every_shard_stood()
    {
        NumberOfStreams = 3;
        StoreOptions(x => x.Projections.Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Async));
        await PublishSingleThreaded();

        // A daemon that is built but never started: its shard never moves
        using var daemon = await theStore.BuildProjectionDaemonAsync();

        var timeout = await Should.ThrowAsync<ProjectionCatchUpTimeoutException>(
            () => WaitForShard(daemon, "TripCustomName:All", NumberOfEvents, typeof(Trip), 2.Seconds()));

        _output.WriteLine(timeout.Message);

        timeout.Wait.ShouldBe($"the TripCustomName:All shard has reached sequence {NumberOfEvents}");
        // "of N" is the store's highest sequence, which a cleaned store does not reset to NumberOfEvents
        timeout.Behind.ShouldHaveSingleItem().ShouldStartWith("'TripCustomName:All' at 0 of ");
        timeout.InnerException.ShouldBeOfType<TimeoutException>();
    }
}
