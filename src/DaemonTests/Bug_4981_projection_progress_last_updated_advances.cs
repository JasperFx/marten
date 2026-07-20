using System;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests;

public record Bug4981Event();

public class Bug4981Stream { public Guid Id { get; set; } }

public partial class Bug4981Projection: SingleStreamProjection<Bug4981Stream, Guid>
{
    public void Apply(Bug4981Event @event, Bug4981Stream projection) { }
}

public class Bug_4981_projection_progress_last_updated_advances: DaemonContext
{
    public Bug_4981_projection_progress_last_updated_advances(ITestOutputHelper output): base(output)
    {
    }

    private async Task<DateTime> lastUpdatedForAsync(string shardName)
    {
        await using var session = theStore.QuerySession();
        var value = await session.Connection
            .CreateCommand(
                $"select last_updated from {theStore.Events.DatabaseSchemaName}.mt_event_progression where name = :name")
            .With("name", shardName)
            .ExecuteScalarAsync();

        return (DateTime)value!;
    }

    [Fact]
    public async Task last_updated_advances_as_a_projection_shard_processes_more_events()
    {
        StoreOptions(x => x.Projections.Add(new Bug4981Projection(), ProjectionLifecycle.Async));

        var agent = await StartDaemon();

        const string shard = "Bug4981Stream:All";

        await using (var session = theStore.LightweightSession())
        {
            for (var i = 0; i < 50; i++)
            {
                session.Events.Append(Guid.NewGuid(), new Bug4981Event());
            }

            await session.SaveChangesAsync();
        }

        await agent.Tracker.WaitForShardState(new ShardState(shard, 50), 30.Seconds());
        var firstUpdated = await lastUpdatedForAsync(shard);

        // ensure a measurable clock gap so the second write's transaction_timestamp() differs
        await Task.Delay(1.Seconds());

        await using (var session = theStore.LightweightSession())
        {
            for (var i = 0; i < 50; i++)
            {
                session.Events.Append(Guid.NewGuid(), new Bug4981Event());
            }

            await session.SaveChangesAsync();
        }

        await agent.Tracker.WaitForShardState(new ShardState(shard, 100), 30.Seconds());
        var secondUpdated = await lastUpdatedForAsync(shard);

        // #4981: before the fix, UpdateProjectionProgress never touched last_updated, so this
        // stayed frozen at the row's insert time and looked exactly like a stalled projection.
        secondUpdated.ShouldBeGreaterThan(firstUpdated);
    }
}
