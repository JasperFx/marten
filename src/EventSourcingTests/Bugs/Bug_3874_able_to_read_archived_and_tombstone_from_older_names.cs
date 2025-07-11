using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3874_able_to_read_archived_and_tombstone_from_older_names : BugIntegrationContext
{
    [Fact]
    public async Task write_then_read_archived()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream(streamId, new Archived("Old"));

        await theSession.SaveChangesAsync();

        theSession.QueueSqlCommand("update bugs.mt_events set mt_dotnet_type = 'Marten.Events.Archived, Marten'");
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamId);

        events.Single().Data.ShouldBeOfType<Archived>().Reason.ShouldBe("Old");
    }

    [Fact]
    public async Task write_then_read_tombstone()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream(streamId, new Tombstone());

        await theSession.SaveChangesAsync();

        theSession.QueueSqlCommand("update bugs.mt_events set mt_dotnet_type = 'Marten.Events.Operations.Tombstone, Marten'");
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamId);

        events.Single().Data.ShouldBeOfType<Tombstone>();
    }


    [Fact]
    public async Task reproduction()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<ReproLiveAgg>();
            opts.Projections.Add<ReproSimpleProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<ReproArchivedProjection>(ProjectionLifecycle.Async);
        });

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        Guid id = CombGuidIdGeneration.NewGuid();
        theSession.Events.StartStream<ReproLiveAgg>(id, new ReproEvent.ReproCreated("some name"));
        await theSession.SaveChangesAsync();

        theSession.Events.Append(id, new ReproEvent.ReproUpdated("other name"));
        await theSession.SaveChangesAsync();

        theSession.Events.Append(id, new Archived("Archived"));
        await theSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(10.Seconds());

        (await theSession.LoadAsync<ReproSimpleDetails>(id)).ShouldBeNull();
        (await theSession.LoadAsync<ReproArchivedDetails>(id)).ShouldNotBeNull().Status.ShouldBe("Archived");

    }
}

public record ReproSimpleDetails(Guid Id, string Name, string Status);
public record ReproArchivedDetails(Guid Id, string Name, string Status);

public record ReproEvent
{
    public record ReproCreated(string Name) : ReproEvent;
    public record ReproUpdated(string UpdatedName) : ReproEvent;
}

public record ReproLiveAgg(Guid Id, string Name)
{
    public static ReproLiveAgg Create(IEvent<ReproEvent.ReproCreated> @event) => new(@event.Id, @event.Data.Name);
    public static ReproLiveAgg Apply(ReproEvent.ReproUpdated @event, ReproLiveAgg current) => current with { Name = @event.UpdatedName };
}

public class ReproSimpleProjection : SingleStreamProjection<ReproSimpleDetails, Guid>
{
    public static ReproSimpleDetails Create(IEvent<ReproEvent.ReproCreated> @event) => new(@event.Id, @event.Data.Name, "Created");
    public static ReproSimpleDetails Apply(ReproEvent.ReproUpdated @event, ReproSimpleDetails current) => current with { Name = @event.UpdatedName, Status = "Updated" };
    public static bool ShouldDelete(Archived _) => true;
}

public class ReproArchivedProjection : SingleStreamProjection<ReproArchivedDetails, Guid>
{
    public ReproArchivedProjection()
    {
        IncludeArchivedEvents = true;
    }

    public static ReproArchivedDetails Create(IEvent<ReproEvent.ReproCreated> @event) => new(@event.Id, @event.Data.Name, "Created");
    public static ReproArchivedDetails Apply(ReproEvent.ReproUpdated @event, ReproArchivedDetails current) => current with { Name = @event.UpdatedName, Status = "Updated" };
    public static ReproArchivedDetails Apply(Archived _, ReproArchivedDetails current) => current with { Status = "Archived" };
}
