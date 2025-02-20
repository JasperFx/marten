using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Aggregation.Rebuilds;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Events.Schema;
using Marten.Internal.Sessions;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using NSubstitute;
using Shouldly;
using StronglyTypedIds;
using Weasel.Core;
using Weasel.Postgresql.Tables;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests;

public class optimized_rebuilds : DaemonContext
{
    public optimized_rebuilds(ITestOutputHelper output): base(output)
    {

    }

    [Fact]
    public async Task add_extra_columns_to_progression_table()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseOptimizedProjectionRebuilds = true;
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        var table = await findExistingTable(EventProgressionTable.Name);

        table.HasColumn("mode").ShouldBeTrue();
        table.HasColumn("rebuild_threshold").ShouldBeTrue();
        table.HasColumn("assigned_node").ShouldBeTrue();
    }

    private async Task<Table> findExistingTable(string tableName)
    {
        using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        return await new Table(new DbObjectName(theStore.Events.DatabaseSchemaName, tableName)).FetchExistingAsync(conn);
    }

    [Fact]
    public async Task adds_the_aggregate_rebuild_table()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseOptimizedProjectionRebuilds = true;
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        var table = await findExistingTable(AggregateRebuildTable.Name);

        table.ShouldNotBeNull();
    }

    [Fact]
    public async Task use_guid_identified_rebuilder_from_scratch()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseOptimizedProjectionRebuilds = true;
            opts.Projections.Add(new TestingSupport.TripProjection(), ProjectionLifecycle.Live);
        });

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        await wipeAllStreamTypeMarkers();

        theStore.Options.Projections.TryFindProjection("Trip", out var source).ShouldBeTrue();

        var projection = source.Build(theStore);

        var builder = new SingleStreamRebuilder<Trip, Guid>(theStore, theStore.Storage.Database, (IAggregationRuntime<Trip, Guid>)projection);

        var daemon = Substitute.For<IDaemonRuntime>();
        daemon.HighWaterMark().Returns(NumberOfEvents);

        await builder.RebuildAllAsync(daemon, new ShardName("Trip", "All"), projection.As<IAggregationRuntime>().Projection, CancellationToken.None);

        await CheckAllExpectedAggregatesAgainstActuals();

        var states = await theStore.Advanced.AllProjectionProgress();
        var state = states.Single(x => x.ShardName == "Trip:All");
        state.Mode.ShouldBe(ShardMode.continuous);
        state.Sequence.ShouldBe(NumberOfEvents);
    }

    [Fact]
    public async Task optimize_build_kicks_in_at_startup()
    {
        // Honestly, walk through this with the debugger to see that it's doing the right thing

        // Publish with NO projection
        NumberOfStreams = 10;
        await PublishSingleThreaded();

        await wipeAllStreamTypeMarkers();

        // Add the projection and restart the store
        StoreOptions(opts =>
        {
            opts.Events.UseOptimizedProjectionRebuilds = true;
            opts.Projections.Add<TestingSupport.TripProjection>(ProjectionLifecycle.Async);
        }, cleanAll:false);

        using var daemon = await StartDaemon();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(5.Seconds());

        await CheckAllExpectedAggregatesAgainstActuals();
    }

    [Fact]
    public async Task use_guid_identified_rebuilder_from_daemon()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseOptimizedProjectionRebuilds = true;
            opts.Projections.Add<TestingSupport.TripProjection>(ProjectionLifecycle.Async);
        });

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        await wipeAllStreamTypeMarkers();

        using var daemon = await StartDaemon();
        await daemon.RebuildProjectionAsync<TestingSupport.TripProjection>(10.Seconds(), CancellationToken.None);

        await CheckAllExpectedAggregatesAgainstActuals();

        var states = await theStore.Advanced.AllProjectionProgress();
        var state = states.Single(x => x.ShardName == "Trip:All");
        state.Mode.ShouldBe(ShardMode.continuous);
        state.Sequence.ShouldBe(NumberOfEvents);
    }

    [Fact]
    public async Task rebuild_with_conjoined_tenancy_against_guid_based_ids()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseOptimizedProjectionRebuilds = true;
            opts.Projections.Add<TestingSupport.TripProjection>(ProjectionLifecycle.Async);
            opts.Schema.For<Trip>().MultiTenanted();
        }, true);

        NumberOfStreams = 10;
        UseMixOfTenants(5);

        await PublishSingleThreaded();

        await wipeAllStreamTypeMarkers();

        theStore.Options.Projections.TryFindProjection("Trip", out var source).ShouldBeTrue();

        var projection = source.Build(theStore);

        var builder = new SingleStreamRebuilder<Trip, Guid>(theStore, theStore.Storage.Database, (IAggregationRuntime<Trip, Guid>)projection);

        var asyncOptions = new AsyncOptions();
        asyncOptions.DeleteViewTypeOnTeardown(typeof(Trip));

        var daemon = Substitute.For<IDaemonRuntime>();
        daemon.HighWaterMark().Returns(NumberOfEvents);

        await builder.RebuildAllAsync(daemon, new ShardName("Trip", "All"), projection.As<IAggregationRuntime>().Projection, CancellationToken.None);

        await CheckAllExpectedAggregatesAgainstActuals("a");
        await CheckAllExpectedAggregatesAgainstActuals("b");

        var states = await theStore.Advanced.AllProjectionProgress();
        var state = states.Single(x => x.ShardName == "Trip:All");
        state.Mode.ShouldBe(ShardMode.continuous);
        state.Sequence.ShouldBe(NumberOfEvents);

    }

    [Fact]
    public async Task SeedAggregateRebuildTable_on_Guid_identified_streams()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseOptimizedProjectionRebuilds = true;
            opts.Projections.Add<TestingSupport.TripProjection>(ProjectionLifecycle.Async);
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        var operation = new SeedAggregateRebuildTable(theStore.Options, typeof(Trip));

        theSession.QueueOperation(operation);
        await theSession.SaveChangesAsync();

        using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var count = (long)await conn.CreateCommand(
            $"select count(*) from {SchemaName}.{AggregateRebuildTable.Name} where stream_type = 'trip'")
            .ExecuteScalarAsync();

        count.ShouldBe(NumberOfStreams);
    }

    [Fact]
    public async Task QueryForNextAggregateIds()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseOptimizedProjectionRebuilds = true;
            opts.Projections.Add<TestingSupport.TripProjection>(ProjectionLifecycle.Async);
        });

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        var operation = new SeedAggregateRebuildTable(theStore.Options, typeof(Trip));

        theSession.QueueOperation(operation);
        await theSession.SaveChangesAsync();

        var handler = new QueryForNextAggregateIds(theStore.Options, typeof(Trip));
        var ids = await theSession.As<DocumentSessionBase>().ExecuteHandlerAsync(handler, CancellationToken.None);

        ids.Count.ShouldBe(NumberOfStreams);
    }

    [Fact]
    public async Task DequeuePendingAggregateRebuilds()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseOptimizedProjectionRebuilds = true;
            opts.Projections.Add<TestingSupport.TripProjection>(ProjectionLifecycle.Async);
        });

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        await wipeAllStreamTypeMarkers();

        var operation = new SeedAggregateRebuildTable(theStore.Options, typeof(Trip));

        theSession.QueueOperation(operation);
        await theSession.SaveChangesAsync();

        var handler = new QueryForNextAggregateIds(theStore.Options, typeof(Trip));
        var ids = await theSession.As<DocumentSessionBase>().ExecuteHandlerAsync(handler, CancellationToken.None);

        var dequeue = new DequeuePendingAggregateRebuilds(theStore.Options, ids.Select(x => x.Number));
        theSession.QueueOperation(dequeue);
        await theSession.SaveChangesAsync();

        ids = await theSession.As<DocumentSessionBase>().ExecuteHandlerAsync(handler, CancellationToken.None);
        ids.Count.ShouldBe(0);


    }


    [Fact]
    public async Task use_guid_identified_rebuilder_from_scratch_using_strong_typed_id()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseOptimizedProjectionRebuilds = true;
            opts.Projections.Add<StrongTripProjection>(ProjectionLifecycle.Async);
        });

        NumberOfStreams = 10;
        await PublishSingleThreaded<StrongTrip>();

        await wipeAllStreamTypeMarkers();

        theStore.Options.Projections.TryFindProjection("StrongTrip", out var source).ShouldBeTrue();

        var projection = source.Build(theStore);

        var builder = new SingleStreamRebuilder<StrongTrip, TripId>(theStore, theStore.Storage.Database, (IAggregationRuntime<StrongTrip, TripId>)projection);

        var daemon = Substitute.For<IDaemonRuntime>();
        daemon.HighWaterMark().Returns(NumberOfEvents);

        await builder.RebuildAllAsync(daemon, new ShardName("StrongTrip", "All"), projection.As<IAggregationRuntime>().Projection, CancellationToken.None);

        await CheckAllExpectedGuidCentricAggregatesAgainstActuals<StrongTrip>(x => x.Id.Value.Value);

        var states = await theStore.Advanced.AllProjectionProgress();
        var state = states.Single(x => x.ShardName == "StrongTrip:All");
        state.Mode.ShouldBe(ShardMode.continuous);
        state.Sequence.ShouldBe(NumberOfEvents);
    }

    [Fact]
    public async Task use_string_identified_rebuilder_from_scratch()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseOptimizedProjectionRebuilds = true;
            opts.Projections.Add<StringTripProjection>(ProjectionLifecycle.Async);
        });

        NumberOfStreams = 10;
        await PublishSingleThreaded<StringTrip>();

        theStore.Options.Projections.TryFindProjection("StringTrip", out var source).ShouldBeTrue();

        var projection = source.Build(theStore);

        var builder = new SingleStreamRebuilder<StringTrip, string>(theStore, theStore.Storage.Database, (IAggregationRuntime<StringTrip, string>)projection);

        var daemon = Substitute.For<IDaemonRuntime>();
        daemon.HighWaterMark().Returns(NumberOfEvents);

        await builder.RebuildAllAsync(daemon, new ShardName("StringTrip", "All"), projection.As<IAggregationRuntime>().Projection, CancellationToken.None);

        await CheckAllExpectedStringCentricAggregatesAgainstActuals<StringTrip>(doc => doc.Id);

        var states = await theStore.Advanced.AllProjectionProgress();
        var state = states.Single(x => x.ShardName == "StringTrip:All");
        state.Mode.ShouldBe(ShardMode.continuous);
        state.Sequence.ShouldBe(NumberOfEvents);
    }

}


[StronglyTypedId(Template.Guid)]
public partial struct TripId;

public class StrongTrip
{
    public TripId? Id { get; set; }

    public int EndedOn { get; set; }

    public double Traveled { get; set; }

    public string State { get; set; }

    public bool Active { get; set; }

    public int StartedOn { get; set; }
    public Guid? RepairShopId { get; set; }

    protected bool Equals(StrongTrip other)
    {
        return Id.Value.Equals(other.Id.Value) && EndedOn == other.EndedOn && Traveled.Equals(other.Traveled) && State == other.State && Active == other.Active && StartedOn == other.StartedOn;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((StrongTrip) obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, EndedOn, Traveled, State, Active, StartedOn);
    }

    public override string ToString()
    {
        return $"{nameof(Id)}: {Id}, {nameof(EndedOn)}: {EndedOn}, {nameof(Traveled)}: {Traveled}, {nameof(State)}: {State}, {nameof(Active)}: {Active}, {nameof(StartedOn)}: {StartedOn}";
    }
}

public class StrongTripProjection: SingleStreamProjection<StrongTrip>
{
    public StrongTripProjection()
    {
        DeleteEvent<TripAborted>();

        DeleteEvent<Breakdown>(x => x.IsCritical);

        DeleteEvent<VacationOver>((trip, _) => trip.Traveled > 1000);
    }

    // These methods can be either public, internal, or private but there's
    // a small performance gain to making them public
    public void Apply(Arrival e, StrongTrip trip) => trip.State = e.State;
    public void Apply(Travel e, StrongTrip trip) => trip.Traveled += e.TotalDistance();

    public void Apply(TripEnded e, StrongTrip trip)
    {
        trip.Active = false;
        trip.EndedOn = e.Day;
    }

    public StrongTrip Create(TripStarted started)
    {
        return new StrongTrip { StartedOn = started.Day, Active = true };
    }
}


public class StringTrip
{
    public string Id { get; set; }

    public int EndedOn { get; set; }

    public double Traveled { get; set; }

    public string State { get; set; }

    public bool Active { get; set; }

    public int StartedOn { get; set; }
    public Guid? RepairShopId { get; set; }

    protected bool Equals(StringTrip other)
    {
        return Id.Equals(other.Id) && EndedOn == other.EndedOn && Traveled.Equals(other.Traveled) && State == other.State && Active == other.Active && StartedOn == other.StartedOn;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((StringTrip) obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, EndedOn, Traveled, State, Active, StartedOn);
    }

    public override string ToString()
    {
        return $"{nameof(Id)}: {Id}, {nameof(EndedOn)}: {EndedOn}, {nameof(Traveled)}: {Traveled}, {nameof(State)}: {State}, {nameof(Active)}: {Active}, {nameof(StartedOn)}: {StartedOn}";
    }
}

public class StringTripProjection: SingleStreamProjection<StringTrip>
{
    public StringTripProjection()
    {
        DeleteEvent<TripAborted>();

        DeleteEvent<Breakdown>(x => x.IsCritical);

        DeleteEvent<VacationOver>((trip, _) => trip.Traveled > 1000);
    }

    // These methods can be either public, internal, or private but there's
    // a small performance gain to making them public
    public void Apply(Arrival e, StringTrip trip) => trip.State = e.State;
    public void Apply(Travel e, StringTrip trip) => trip.Traveled += e.TotalDistance();

    public void Apply(TripEnded e, StringTrip trip)
    {
        trip.Active = false;
        trip.EndedOn = e.Day;
    }

    public StringTrip Create(TripStarted started)
    {
        return new StringTrip { StartedOn = started.Day, Active = true };
    }
}
