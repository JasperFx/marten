using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace DaemonTests.Bugs;

public class Bug_2597_rebuilding_a_projection_with_no_matching_events_but_other_non_matching_events : BugIntegrationContext
{
    [Fact]
    public async Task do_not_blow_up()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<UsesAEventOnly>(SnapshotLifecycle.Async);
            opts.Projections.Snapshot<OtherAggregate>(SnapshotLifecycle.Inline);
        });

        using (var session = theStore.LightweightSession())
        {
            session.Events.Append(Guid.NewGuid(), new BEvent(), new CEvent(), new DEvent());
            session.Events.Append(Guid.NewGuid(), new BEvent(), new CEvent(), new DEvent());
            session.Events.Append(Guid.NewGuid(), new BEvent(), new CEvent(), new DEvent());
            session.Events.Append(Guid.NewGuid(), new BEvent(), new CEvent(), new DEvent());
            session.Events.Append(Guid.NewGuid(), new BEvent(), new CEvent(), new DEvent());

            await session.SaveChangesAsync();
        }

        using var daemon = await theStore.BuildProjectionDaemonAsync();

        await daemon.RebuildProjectionAsync<UsesAEventOnly>(CancellationToken.None);
    }

    [Fact]
    public async Task Bug_2519_actually_finish_the_rebuild()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<UsesAEventOnly>(SnapshotLifecycle.Async);
            opts.Projections.Snapshot<OtherAggregate>(SnapshotLifecycle.Inline);
        });

        using var daemon = await theStore.BuildProjectionDaemonAsync();

        await daemon.RebuildProjectionAsync<UsesAEventOnly>(CancellationToken.None);
    }
}

public class UsesAEventOnly
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public void Apply(AEvent e) => Count++;
}

public class OtherAggregate
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public void Apply(AEvent e) => Count++;
    public void Apply(BEvent e) => Count++;
    public void Apply(CEvent e) => Count++;
}
