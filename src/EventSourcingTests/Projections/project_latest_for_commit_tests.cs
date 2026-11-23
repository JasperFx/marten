#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Exceptions;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using StronglyTypedIds;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Projections;

#region sample_project_latest_for_commit_aggregate

public record ShipmentStarted(string Origin);

public record ShipmentScanned(string Location);

// Copies the metadata of the last event it applied
public abstract record TrackedShipment
{
    public int Scans { get; set; }
    public long LastEventVersion { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public string? LastModifiedBy { get; set; }
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public string? TenantId { get; set; }

    protected void track(IEvent e)
    {
        LastEventVersion = e.Version;
        LastModified = e.Timestamp;
        LastModifiedBy = e.UserName;
        CorrelationId = e.CorrelationId;
        CausationId = e.CausationId;
        TenantId = e.TenantId;
    }
}

public record Shipment: TrackedShipment
{
    public Guid Id { get; set; }

    public static Shipment Create(IEvent<ShipmentStarted> e)
    {
        var shipment = new Shipment();
        shipment.track(e);
        return shipment;
    }

    public void Apply(IEvent<ShipmentScanned> e)
    {
        Scans++;
        track(e);
    }
}

#endregion

public record KeyedShipment: TrackedShipment
{
    public string Id { get; set; } = "";

    public static KeyedShipment Create(IEvent<ShipmentStarted> e)
    {
        var shipment = new KeyedShipment();
        shipment.track(e);
        return shipment;
    }

    public void Apply(IEvent<ShipmentScanned> e)
    {
        Scans++;
        track(e);
    }
}

[StronglyTypedId(Template.Guid)]
public readonly partial struct ShipmentId;

public record TypedShipment: TrackedShipment
{
    [JsonInclude] public ShipmentId? Id { get; set; }

    public static TypedShipment Create(IEvent<ShipmentStarted> e)
    {
        var shipment = new TypedShipment { Id = new ShipmentId(e.StreamId) };
        shipment.track(e);
        return shipment;
    }

    public void Apply(IEvent<ShipmentScanned> e)
    {
        Scans++;
        track(e);
    }
}

public record ShipmentLabelled(string Label);

public record LabelledShipment
{
    public Guid Id { get; set; }

    [NaturalKey]
    public string? Label { get; set; }

    public int Scans { get; set; }

    [NaturalKeySource]
    public void Apply(ShipmentLabelled e) => Label = e.Label;

    public void Apply(ShipmentScanned e) => Scans++;
}

public partial class GlobalShipmentProjection: SingleStreamProjection<Shipment, Guid>
{
    public Shipment Create(IEvent<ShipmentStarted> e) => Shipment.Create(e);

    public void Apply(IEvent<ShipmentScanned> e, Shipment shipment) => shipment.Apply(e);
}

public enum GuardChange
{
    FetchForWritingAgain,
    AppendWithExpectedVersion,
    AppendOptimistic,
    AppendExclusive
}

public enum PendingShape
{
    FetchForWriting,
    StartStream,
    AppendWithExpectedVersion
}

public class project_latest_for_commit_tests: OneOffConfigurationsContext
{
    // Sub-microsecond ticks, so a timestamp the call did not bring to Postgres precision differs from the stored one
    private readonly FakeTimeProvider _clock =
        new(new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero).AddTicks(1234567));

    private void configure(EventAppendMode appendMode, Action<StoreOptions>? configure = null)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = appendMode;
            opts.Events.TimeProvider = _clock;
            opts.Events.MetadataConfig.UserNameEnabled = true;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            configure?.Invoke(opts);
        });
    }

    private static void register<T>(StoreOptions opts, ProjectionLifecycle lifecycle) where T : class
    {
        switch (lifecycle)
        {
            case ProjectionLifecycle.Live:
                opts.Projections.LiveStreamAggregation<T>();
                break;
            case ProjectionLifecycle.Inline:
                opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline);
                break;
            default:
                opts.Projections.Snapshot<T>(SnapshotLifecycle.Async);
                break;
        }
    }

    private static void stampMetadata(IDocumentSession session, string suffix)
    {
        session.LastModifiedBy = "user-" + suffix;
        session.CorrelationId = "correlation-" + suffix;
        session.CausationId = "causation-" + suffix;
    }

    private async Task<Guid> startShipment()
    {
        var id = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        stampMetadata(session, "seed");
        session.Events.StartStream<Shipment>(id, new ShipmentStarted("York"), new ShipmentScanned("Selby"));
        await session.SaveChangesAsync();
        return id;
    }

    private async Task shouldMatchTheCommit<T>(T? returned, Guid id, string? tenantId = null) where T : class
    {
        await using var session = tenantId == null
            ? theStore.LightweightSession()
            : theStore.LightweightSession(tenantId);
        var fromStoredEvents = await session.Events.AggregateStreamAsync<T>(id);
        var latest = await session.Events.FetchLatest<T>(id);

        returned.ShouldNotBeNull();
        returned.ShouldBe(fromStoredEvents);
        latest.ShouldBe(fromStoredEvents);
    }

    public static IEnumerable<object[]> AgreementCases()
    {
        var lifecycles = new[] { ProjectionLifecycle.Live, ProjectionLifecycle.Inline, ProjectionLifecycle.Async };

        foreach (var lifecycle in lifecycles)
        foreach (var appendMode in new[] { EventAppendMode.Rich, EventAppendMode.QuickWithServerTimestamps })
        foreach (var shape in Enum.GetValues<PendingShape>())
        {
            yield return [lifecycle, appendMode, shape, true, true];
        }

        foreach (var lifecycle in lifecycles)
        {
            // Under QuickWithServerTimestamps a new stream goes through the bulk append function only without strict
            // stream identity enforcement, and through an insert per event with it
            yield return [lifecycle, EventAppendMode.QuickWithServerTimestamps, PendingShape.StartStream, true, false];

            // The identity map changes how each plan reads a stream fetched for writing
            yield return [lifecycle, EventAppendMode.QuickWithServerTimestamps, PendingShape.FetchForWriting, false, true];
        }

        // With the identity map off, the Inline plan also loads an existing document through storage
        yield return
        [
            ProjectionLifecycle.Inline, EventAppendMode.QuickWithServerTimestamps,
            PendingShape.AppendWithExpectedVersion, false, true
        ];
    }

    [Theory]
    [MemberData(nameof(AgreementCases))]
    public async Task returns_the_aggregate_the_commit_writes(ProjectionLifecycle lifecycle,
        EventAppendMode appendMode, PendingShape shape, bool useIdentityMap, bool strictStreamIdentity)
    {
        configure(appendMode, opts =>
        {
            register<Shipment>(opts, lifecycle);
            opts.Events.UseIdentityMapForAggregates = useIdentityMap;
            opts.Events.EnableStrictStreamIdentityEnforcement = strictStreamIdentity;
        });

        var id = shape == PendingShape.StartStream ? Guid.NewGuid() : await startShipment();

        await using var session = theStore.LightweightSession();
        stampMetadata(session, "call");

        switch (shape)
        {
            case PendingShape.FetchForWriting:
                var stream = await session.Events.FetchForWriting<Shipment>(id);
                stream.AppendOne(new ShipmentScanned("Leeds"));
                break;
            case PendingShape.StartStream:
                session.Events.StartStream<Shipment>(id, new ShipmentStarted("York"), new ShipmentScanned("Leeds"));
                break;
            case PendingShape.AppendWithExpectedVersion:
                session.Events.Append(id, 3, new ShipmentScanned("Leeds"));
                break;
        }

        var returned = await session.Events.ProjectLatestForCommit<Shipment>(id);

        _clock.Advance(5.Seconds());
        await session.SaveChangesAsync();

        await shouldMatchTheCommit(returned, id);
    }

    [Theory]
    [InlineData(EventAppendMode.Rich, true)]
    [InlineData(EventAppendMode.Rich, false)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps, true)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps, false)]
    public async Task async_snapshot_built_by_the_daemon_equals_the_returned_aggregate(EventAppendMode appendMode,
        bool snapshotExistsBeforeTheCall)
    {
        configure(appendMode, opts => register<Shipment>(opts, ProjectionLifecycle.Async));

        var id = await startShipment();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        if (snapshotExistsBeforeTheCall)
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(15.Seconds());
            await daemon.StopAllAsync();
        }

        Shipment? returned;
        await using (var session = theStore.LightweightSession())
        {
            stampMetadata(session, "call");
            var stream = await session.Events.FetchForWriting<Shipment>(id);
            stream.AppendOne(new ShipmentScanned("Leeds"));

            returned = await session.Events.ProjectLatestForCommit<Shipment>(id);

            _clock.Advance(5.Seconds());
            await session.SaveChangesAsync();
        }

        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(15.Seconds());

        await using var query = theStore.QuerySession();
        var snapshot = await query.LoadAsync<Shipment>(id);
        snapshot.ShouldBe(returned);

        await shouldMatchTheCommit(returned, id);
    }

    [Theory]
    [InlineData(ProjectionLifecycle.Live)]
    [InlineData(ProjectionLifecycle.Inline)]
    [InlineData(ProjectionLifecycle.Async)]
    public async Task calling_again_after_another_append_covers_the_new_event(ProjectionLifecycle lifecycle)
    {
        configure(EventAppendMode.QuickWithServerTimestamps, opts => register<Shipment>(opts, lifecycle));

        var id = await startShipment();

        await using var session = theStore.LightweightSession();
        stampMetadata(session, "call");
        var stream = await session.Events.FetchForWriting<Shipment>(id);
        stream.AppendOne(new ShipmentScanned("Leeds"));

        var first = await session.Events.ProjectLatestForCommit<Shipment>(id);
        _clock.Advance(1.Seconds());
        var second = await session.Events.ProjectLatestForCommit<Shipment>(id);
        first.ShouldNotBeNull();
        second.ShouldBe(first);

        stream.AppendOne(new ShipmentScanned("Hull"));
        _clock.Advance(1.Seconds());
        var third = await session.Events.ProjectLatestForCommit<Shipment>(id);
        third.ShouldNotBeNull();
        third.Scans.ShouldBe(first.Scans + 1);

        _clock.Advance(5.Seconds());
        await session.SaveChangesAsync();

        await shouldMatchTheCommit(third, id);
    }

    [Theory]
    [InlineData(EventAppendMode.Rich)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task the_commit_keeps_the_values_the_call_assigned(EventAppendMode appendMode)
    {
        configure(appendMode, opts => register<Shipment>(opts, ProjectionLifecycle.Live));

        var shipmentId = await startShipment();

        await using var session = theStore.LightweightSession();
        stampMetadata(session, "call");

        #region sample_project_latest_for_commit

        var stream = await session.Events.FetchForWriting<Shipment>(shipmentId);
        stream.AppendOne(new ShipmentScanned("Leeds"));

        // The pending event's version, timestamp, user name, correlation id and causation id are assigned now,
        // and SaveChangesAsync() keeps them
        var shipment = await session.Events.ProjectLatestForCommit<Shipment>(shipmentId);

        #endregion

        // Everything the commit would assign is different by the time it runs
        _clock.Advance(5.Seconds());
        stampMetadata(session, "commit");
        await session.SaveChangesAsync();

        shipment.ShouldNotBeNull();
        shipment.LastEventVersion.ShouldBe(3);
        shipment.LastModifiedBy.ShouldBe("user-call");

        await using var query = theStore.QuerySession();
        var stored = (await query.Events.FetchStreamAsync(shipmentId)).Last();
        stored.Version.ShouldBe(3);
        stored.Timestamp.ShouldBe(shipment.LastModified);
        stored.UserName.ShouldBe("user-call");
        stored.CorrelationId.ShouldBe("correlation-call");
        stored.CausationId.ShouldBe("causation-call");

        await shouldMatchTheCommit(shipment, shipmentId);
    }

    [Theory]
    [InlineData(EventAppendMode.Rich)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task session_metadata_unset_at_the_call_is_filled_at_commit(EventAppendMode appendMode)
    {
        configure(appendMode, opts => register<Shipment>(opts, ProjectionLifecycle.Live));

        var id = await startShipment();

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<Shipment>(id);
        stream.AppendOne(new ShipmentScanned("Leeds"));

        var returned = await session.Events.ProjectLatestForCommit<Shipment>(id);

        stampMetadata(session, "commit");
        await session.SaveChangesAsync();

        returned.ShouldNotBeNull();
        returned.LastModifiedBy.ShouldBeNull();
        returned.CorrelationId.ShouldBeNull();
        returned.CausationId.ShouldBeNull();

        await using var query = theStore.QuerySession();
        var stored = await query.Events.AggregateStreamAsync<Shipment>(id);
        stored.ShouldBe(returned with
        {
            LastModifiedBy = "user-commit", CorrelationId = "correlation-commit", CausationId = "causation-commit"
        });
    }

    public static IEnumerable<object[]> CallerTimestamps()
    {
        foreach (var appendMode in new[] { EventAppendMode.Rich, EventAppendMode.QuickWithServerTimestamps })
        {
            // Npgsql truncates toward 2000-01-01, so before 2000 this one rounds up, not down
            var before2000 = new DateTimeOffset(1999, 12, 31, 23, 59, 59, TimeSpan.Zero);
            yield return [appendMode, before2000.AddTicks(1234567), before2000.AddTicks(1234570)];

            var after2000 = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
            yield return [appendMode, after2000.AddTicks(7654321), after2000.AddTicks(7654320)];

            // Written as infinity
            yield return [appendMode, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue];
        }
    }

    [Theory]
    [MemberData(nameof(CallerTimestamps))]
    public async Task metadata_set_on_a_pending_event_is_kept(EventAppendMode appendMode,
        DateTimeOffset timestamp, DateTimeOffset stored)
    {
        configure(appendMode, opts => register<Shipment>(opts, ProjectionLifecycle.Live));

        var id = Guid.NewGuid();

        await using var session = theStore.LightweightSession();
        stampMetadata(session, "session");
        var action = session.Events.StartStream<Shipment>(id, new ShipmentStarted("York"), new ShipmentScanned("Leeds"));
        action.Events[1].Timestamp = timestamp;
        action.Events[1].UserName = "caller";

        // What the database stores for the same timestamp with no call involved
        var controlId = Guid.NewGuid();
        session.Events.StartStream<Shipment>(controlId, new ShipmentStarted("York"), new ShipmentScanned("Leeds"))
            .Events[1].Timestamp = timestamp;

        var returned = await session.Events.ProjectLatestForCommit<Shipment>(id);
        var timestampsAtTheCall = action.Events.Select(x => x.Timestamp.UtcTicks).ToArray();

        _clock.Advance(5.Seconds());
        await session.SaveChangesAsync();

        returned.ShouldNotBeNull();
        returned.LastModifiedBy.ShouldBe("caller");
        returned.LastModified.UtcTicks.ShouldBe(stored.UtcTicks);

        await using var query = theStore.QuerySession();
        var events = await query.Events.FetchStreamAsync(id);
        events.Select(x => x.Timestamp.UtcTicks).ToArray().ShouldBe(timestampsAtTheCall);
        (await query.Events.FetchStreamAsync(controlId))[1].Timestamp.UtcTicks.ShouldBe(stored.UtcTicks);

        await shouldMatchTheCommit(returned, id);
    }

    [Theory]
    [InlineData(EventAppendMode.Rich)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task for_tenant_session_on_a_conjoined_store(EventAppendMode appendMode)
    {
        configure(appendMode, opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
            register<Shipment>(opts, ProjectionLifecycle.Inline);
        });

        var id = Guid.NewGuid();

        await using var session = theStore.LightweightSession("red");
        stampMetadata(session, "root");
        var blue = session.ForTenant("blue");
        blue.Events.StartStream<Shipment>(id, new ShipmentStarted("York"), new ShipmentScanned("Leeds"));

        var returned = await blue.Events.ProjectLatestForCommit<Shipment>(id);

        _clock.Advance(5.Seconds());
        await session.SaveChangesAsync();

        // SaveChangesAsync() runs on the root session, so the events carry its metadata
        returned.ShouldNotBeNull();
        returned.TenantId.ShouldBe("blue");
        returned.LastModifiedBy.ShouldBe("user-root");

        await shouldMatchTheCommit(returned, id, "blue");
    }

    [Theory]
    [InlineData(EventAppendMode.Rich)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task global_aggregate_through_a_for_tenant_session(EventAppendMode appendMode)
    {
        configure(appendMode, opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.AddGlobalProjection(new GlobalShipmentProjection(), ProjectionLifecycle.Inline);
        });

        var id = Guid.NewGuid();

        await using var session = theStore.LightweightSession("red");
        stampMetadata(session, "root");
        var blue = session.ForTenant("blue");
        blue.Events.StartStream<Shipment>(id, new ShipmentStarted("York"), new ShipmentScanned("Leeds"));

        var returned = await blue.Events.ProjectLatestForCommit<Shipment>(id);

        _clock.Advance(5.Seconds());
        await session.SaveChangesAsync();

        returned.ShouldNotBeNull();

        // The inline projection folds the events as the commit prepared them, their tenant included. That tenant
        // depends on the append mode, so the answer is compared with what this mode wrote
        await using var query = theStore.QuerySession();
        var written = await query.LoadAsync<Shipment>(id);
        written.ShouldBe(returned);

        var stored = (await query.Events.FetchStreamAsync(id)).Last();
        stored.TenantId.ShouldBe(StorageConstants.DefaultTenantId);
        stored.Version.ShouldBe(returned.LastEventVersion);
        stored.Timestamp.ShouldBe(returned.LastModified);
        stored.UserName.ShouldBe(returned.LastModifiedBy);
        stored.CorrelationId.ShouldBe(returned.CorrelationId);
        stored.CausationId.ShouldBe(returned.CausationId);

        var fromStoredEvents = await query.Events.AggregateStreamAsync<Shipment>(id);
        fromStoredEvents.ShouldBe(returned with { TenantId = StorageConstants.DefaultTenantId });
    }

    [Fact]
    public async Task string_keyed_stream()
    {
        configure(EventAppendMode.QuickWithServerTimestamps, opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            register<KeyedShipment>(opts, ProjectionLifecycle.Inline);
        });

        var key = "shipment-" + Guid.NewGuid();
        await using (var seed = theStore.LightweightSession())
        {
            seed.Events.StartStream<KeyedShipment>(key, new ShipmentStarted("York"), new ShipmentScanned("Selby"));
            await seed.SaveChangesAsync();
        }

        await using var session = theStore.LightweightSession();
        stampMetadata(session, "call");
        var stream = await session.Events.FetchForWriting<KeyedShipment>(key);
        stream.AppendOne(new ShipmentScanned("Leeds"));

        var returned = await session.Events.ProjectLatestForCommit<KeyedShipment>(key);

        _clock.Advance(5.Seconds());
        await session.SaveChangesAsync();

        await using var query = theStore.LightweightSession();
        var fromStoredEvents = await query.Events.AggregateStreamAsync<KeyedShipment>(key);
        var latest = await query.Events.FetchLatest<KeyedShipment>(key);
        returned.ShouldNotBeNull();
        returned.ShouldBe(fromStoredEvents);
        latest.ShouldBe(fromStoredEvents);
    }

    [Theory]
    [InlineData(ProjectionLifecycle.Live)]
    [InlineData(ProjectionLifecycle.Async)]
    public async Task strong_typed_id_aggregate(ProjectionLifecycle lifecycle)
    {
        configure(EventAppendMode.QuickWithServerTimestamps, opts =>
        {
            opts.UseSystemTextJsonForSerialization(new JsonSerializerOptions { IncludeFields = true });
            register<TypedShipment>(opts, lifecycle);
        });

        var id = Guid.NewGuid();
        await using (var seed = theStore.LightweightSession())
        {
            seed.Events.StartStream<TypedShipment>(id, new ShipmentStarted("York"), new ShipmentScanned("Selby"));
            await seed.SaveChangesAsync();
        }

        await using var session = theStore.LightweightSession();
        stampMetadata(session, "call");
        var stream = await session.Events.FetchForWriting<TypedShipment>(id);
        stream.AppendOne(new ShipmentScanned("Leeds"));

        var returned = await session.Events.ProjectLatestForCommit<TypedShipment>(id);

        _clock.Advance(5.Seconds());
        await session.SaveChangesAsync();

        await shouldMatchTheCommit(returned, id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task without_pending_events_it_is_fetch_latest(bool alwaysEnforceConsistency)
    {
        configure(EventAppendMode.QuickWithServerTimestamps, opts => register<Shipment>(opts, ProjectionLifecycle.Live));

        var id = await startShipment();

        await using var session = theStore.LightweightSession();
        var nothingPending = await session.Events.ProjectLatestForCommit<Shipment>(id);

        var stream = await session.Events.FetchForWriting<Shipment>(id);
        stream.AlwaysEnforceConsistency = alwaysEnforceConsistency;
        var nothingAppended = await session.Events.ProjectLatestForCommit<Shipment>(id);

        await session.SaveChangesAsync();

        await using var query = theStore.LightweightSession();
        var latest = await query.Events.FetchLatest<Shipment>(id);
        latest.ShouldNotBeNull();
        nothingPending.ShouldBe(latest);
        nothingAppended.ShouldBe(latest);
        (await query.Events.FetchStreamStateAsync(id))!.Version.ShouldBe(2);
    }

    [Theory]
    [InlineData(EventAppendMode.Rich)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task starting_a_stream_whose_id_already_exists(EventAppendMode appendMode)
    {
        configure(appendMode, opts => register<Shipment>(opts, ProjectionLifecycle.Live));

        var id = await startShipment();

        await using var session = theStore.LightweightSession();
        var action = session.Events.StartStream<Shipment>(id, new ShipmentScanned("Leeds"));
        await session.Events.FetchForWriting<Shipment>(id);

        var call = session.Events.ProjectLatestForCommit<Shipment>(id);
        if (appendMode == EventAppendMode.Rich)
        {
            await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(call.AsTask());
            action.Events.Single().Version.ShouldBe(1);
        }
        else
        {
            (await call).ShouldNotBeNull();
            await Should.ThrowAsync<ExistingStreamIdCollisionException>(() => session.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task refuses_a_natural_key_without_touching_the_pending_events()
    {
        configure(EventAppendMode.QuickWithServerTimestamps,
            opts => register<LabelledShipment>(opts, ProjectionLifecycle.Live));

        var id = Guid.NewGuid();
        var label = "label-" + Guid.NewGuid();
        await using (var seed = theStore.LightweightSession())
        {
            seed.Events.StartStream<LabelledShipment>(id, new ShipmentLabelled(label), new ShipmentScanned("Selby"));
            await seed.SaveChangesAsync();
        }

        await using var session = theStore.LightweightSession();
        stampMetadata(session, "call");
        var stream = await session.Events.FetchForWriting<LabelledShipment, string>(label);
        stream.AppendOne(new ShipmentScanned("Leeds"));
        var action = session.PendingChanges.Streams().Single();

        var call = session.Events.ProjectLatestForCommit<LabelledShipment>(label);
        var ex = await Should.ThrowAsync<InvalidOperationException>(call.AsTask());
        ex.Message.ShouldContain("natural key");

        var pending = action.Events.Single();
        pending.Version.ShouldBe(0);
        pending.Timestamp.ShouldBe(default);
        pending.UserName.ShouldBeNull();
        action.ExpectedVersionOnServer.ShouldBe(2);

        _clock.Advance(5.Seconds());
        stampMetadata(session, "commit");
        await session.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        var stored = (await query.Events.FetchStreamAsync(id)).Last();
        stored.Version.ShouldBe(3);
        stored.UserName.ShouldBe("user-commit");
        (await query.Events.AggregateStreamAsync<LabelledShipment>(id)).ShouldNotBeNull().Scans.ShouldBe(2);
    }

    [Theory]
    [InlineData(EventAppendMode.Quick, "EventAppendMode.Quick")]
    [InlineData(EventAppendMode.QuickWithServerTimestamps, "FetchForWriting()")]
    public async Task refuses_without_touching_the_pending_events(EventAppendMode appendMode, string reason)
    {
        configure(appendMode, opts => register<Shipment>(opts, ProjectionLifecycle.Live));

        var id = await startShipment();

        await using var session = theStore.LightweightSession();
        stampMetadata(session, "call");

        // Quick refuses even a version-guarded stream; an Append with no expected version is refused in any mode
        var action = appendMode == EventAppendMode.Quick
            ? session.Events.Append(id, 3, new ShipmentScanned("Leeds"))
            : session.Events.Append(id, new ShipmentScanned("Leeds"));
        var expectedVersion = action.ExpectedVersionOnServer;

        // The refusal comes from the returned task, not from the call itself
        var call = session.Events.ProjectLatestForCommit<Shipment>(id);
        var ex = await Should.ThrowAsync<InvalidOperationException>(call.AsTask());
        ex.Message.ShouldContain(reason);

        var pending = action.Events.Single();
        pending.Version.ShouldBe(0);
        pending.Timestamp.ShouldBe(default);
        pending.UserName.ShouldBeNull();
        pending.CorrelationId.ShouldBeNull();
        pending.CausationId.ShouldBeNull();
        action.ExpectedVersionOnServer.ShouldBe(expectedVersion);

        // So the commit assigns everything itself, as it would have without the call
        _clock.Advance(5.Seconds());
        stampMetadata(session, "commit");
        await session.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        var stored = (await query.Events.FetchStreamAsync(id)).Last();
        stored.Version.ShouldBe(3);
        stored.UserName.ShouldBe("user-commit");
        stored.CorrelationId.ShouldBe("correlation-commit");
        stored.CausationId.ShouldBe("causation-commit");
        if (appendMode != EventAppendMode.Quick)
        {
            // The clock's sub-microsecond ticks, dropped by the database
            stored.Timestamp.ShouldBe(_clock.GetUtcNow().AddTicks(-7));
        }
    }

    private async Task competingScan(Guid id)
    {
        await using var other = theStore.LightweightSession();
        other.Events.Append(id, new ShipmentScanned("Elsewhere"));
        await other.SaveChangesAsync();
    }

    private static async Task changeExpectedVersion(IDocumentSession session, Guid id, GuardChange change)
    {
        switch (change)
        {
            case GuardChange.FetchForWritingAgain:
                await session.Events.FetchForWriting<Shipment>(id);
                break;
            case GuardChange.AppendWithExpectedVersion:
                // Two events pending, ending at version 5, so the stream is expected at version 3
                session.Events.Append(id, 5, new ShipmentScanned("Hull"));
                break;
            case GuardChange.AppendOptimistic:
                await session.Events.AppendOptimistic(id, new ShipmentScanned("Hull"));
                break;
            case GuardChange.AppendExclusive:
                await session.Events.AppendExclusive(id, new ShipmentScanned("Hull"));
                break;
        }
    }

    public static IEnumerable<object[]> GuardChanges()
    {
        foreach (var appendMode in new[] { EventAppendMode.Rich, EventAppendMode.QuickWithServerTimestamps })
        foreach (var change in Enum.GetValues<GuardChange>())
        {
            yield return [appendMode, change];
        }
    }

    [Theory]
    [MemberData(nameof(GuardChanges))]
    public async Task commit_refuses_a_stream_whose_expected_version_changed_after_the_call(EventAppendMode appendMode,
        GuardChange change)
    {
        configure(appendMode, opts => register<Shipment>(opts, ProjectionLifecycle.Live));

        var id = await startShipment();

        await using var session = theStore.LightweightSession();
        stampMetadata(session, "call");
        var stream = await session.Events.FetchForWriting<Shipment>(id);
        stream.AppendOne(new ShipmentScanned("Leeds"));

        await session.Events.ProjectLatestForCommit<Shipment>(id);

        await competingScan(id);
        await changeExpectedVersion(session, id, change);

        var ex = await Should.ThrowAsync<InvalidStreamOperationException>(() => session.SaveChangesAsync());
        ex.Message.ShouldContain("ProjectLatestForCommit() again");

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamStateAsync(id))!.Version.ShouldBe(3);
    }

    [Theory]
    [InlineData(EventAppendMode.Rich, GuardChange.FetchForWritingAgain)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps, GuardChange.AppendWithExpectedVersion)]
    public async Task calling_again_after_the_expected_version_changed_lets_the_commit_through(
        EventAppendMode appendMode, GuardChange change)
    {
        configure(appendMode, opts => register<Shipment>(opts, ProjectionLifecycle.Live));

        var id = await startShipment();

        await using var session = theStore.LightweightSession();
        stampMetadata(session, "call");
        var stream = await session.Events.FetchForWriting<Shipment>(id);
        stream.AppendOne(new ShipmentScanned("Leeds"));

        await session.Events.ProjectLatestForCommit<Shipment>(id);

        await competingScan(id);
        await changeExpectedVersion(session, id, change);

        var returned = await session.Events.ProjectLatestForCommit<Shipment>(id);

        _clock.Advance(5.Seconds());
        await session.SaveChangesAsync();

        await shouldMatchTheCommit(returned, id);
    }

    [Theory]
    [InlineData(EventAppendMode.Rich)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task appending_to_the_guarded_stream_after_the_call_still_commits(EventAppendMode appendMode)
    {
        configure(appendMode, opts => register<Shipment>(opts, ProjectionLifecycle.Live));

        var id = await startShipment();

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<Shipment>(id);
        stream.AppendOne(new ShipmentScanned("Leeds"));

        var returned = await session.Events.ProjectLatestForCommit<Shipment>(id);

        stream.AppendOne(new ShipmentScanned("Hull"));
        await session.SaveChangesAsync();

        returned.ShouldNotBeNull();
        await using var query = theStore.QuerySession();
        var stored = await query.Events.AggregateStreamAsync<Shipment>(id);
        stored.ShouldNotBeNull();
        stored.Scans.ShouldBe(returned.Scans + 1);
        stored.LastEventVersion.ShouldBe(4);
    }

    [Fact]
    public async Task a_session_that_never_called_is_free_to_fetch_for_writing_again()
    {
        configure(EventAppendMode.QuickWithServerTimestamps, opts => register<Shipment>(opts, ProjectionLifecycle.Live));

        var id = await startShipment();

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<Shipment>(id);
        stream.AppendOne(new ShipmentScanned("Leeds"));

        await competingScan(id);
        await session.Events.FetchForWriting<Shipment>(id);
        await session.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamStateAsync(id))!.Version.ShouldBe(4);
    }

    [Fact]
    public async Task ejecting_the_pending_changes_forgets_the_call()
    {
        configure(EventAppendMode.QuickWithServerTimestamps, opts => register<Shipment>(opts, ProjectionLifecycle.Live));

        var id = await startShipment();

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<Shipment>(id);
        stream.AppendOne(new ShipmentScanned("Leeds"));
        await session.Events.ProjectLatestForCommit<Shipment>(id);

        session.EjectAllPendingChanges();
        await competingScan(id);

        stream = await session.Events.FetchForWriting<Shipment>(id);
        stream.AppendOne(new ShipmentScanned("Hull"));
        await session.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamStateAsync(id))!.Version.ShouldBe(4);
    }

    private class CommandCounter: IMartenLogger, IMartenSessionLogger
    {
        private int _count;

        public int Count => _count;

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public IMartenSessionLogger StartSession(IQuerySession session) => this;
        public void SchemaChange(string sql) { }
        public void OnBeforeExecute(NpgsqlCommand command) => Interlocked.Increment(ref _count);
        public void OnBeforeExecute(NpgsqlBatch batch) => Interlocked.Increment(ref _count);
        public void LogSuccess(NpgsqlCommand command) { }
        public void LogFailure(NpgsqlCommand command, Exception ex) { }
        public void LogSuccess(NpgsqlBatch batch) { }
        public void LogFailure(NpgsqlBatch batch, Exception ex) { }
        public void LogFailure(Exception ex, string message) { }
        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit) { }
    }

    private async Task<(long streams, long tombstones, long lastSequence)> readEventStorage()
    {
        await theStore.Options.EventGraph.WaitForPendingTombstonesAsync();

        var schema = theStore.Options.Events.DatabaseSchemaName;
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        async Task<long> scalar(string sql) => Convert.ToInt64(await conn.CreateCommand(sql).ExecuteScalarAsync());

        return (await scalar($"select count(*) from {schema}.mt_streams"),
            await scalar($"select count(*) from {schema}.mt_events where type = 'tombstone'"),
            await scalar($"select last_value from {schema}.mt_events_sequence"));
    }

    [Theory]
    [InlineData(EventAppendMode.Rich, false)]
    [InlineData(EventAppendMode.Rich, true)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps, false)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps, true)]
    public async Task a_refused_commit_sends_nothing_to_the_database(EventAppendMode appendMode,
        bool afterAFailedCommit)
    {
        var commands = new CommandCounter();
        configure(appendMode, opts =>
        {
            register<Shipment>(opts, ProjectionLifecycle.Live);
            opts.Logger(commands);
        });

        var id = await startShipment();

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<Shipment>(id);
        stream.AppendOne(new ShipmentScanned("Leeds"));
        await session.Events.ProjectLatestForCommit<Shipment>(id);

        await competingScan(id);
        if (afterAFailedCommit)
        {
            await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(() => session.SaveChangesAsync());
        }

        await session.Events.FetchForWriting<Shipment>(id);

        var before = await readEventStorage();
        commands.Reset();

        await Should.ThrowAsync<InvalidStreamOperationException>(() => session.SaveChangesAsync());

        // A tombstone write is asynchronous, so wait for it before asserting nothing was written
        await theStore.Options.EventGraph.WaitForPendingTombstonesAsync();
        commands.Count.ShouldBe(0);
        (await readEventStorage()).ShouldBe(before);
    }

    [Theory]
    [InlineData(EventAppendMode.Rich, true)]
    [InlineData(EventAppendMode.Rich, false)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps, true)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps, false)]
    public async Task retrying_on_the_same_session_after_a_failed_commit(EventAppendMode appendMode, bool callAgain)
    {
        configure(appendMode, opts => register<Shipment>(opts, ProjectionLifecycle.Live));

        var id = await startShipment();

        await using var session = theStore.LightweightSession();
        stampMetadata(session, "call");
        var stream = await session.Events.FetchForWriting<Shipment>(id);
        stream.AppendOne(new ShipmentScanned("Leeds"));
        var returned = await session.Events.ProjectLatestForCommit<Shipment>(id);

        // A competing commit with the expected version left alone is the usual concurrency failure
        await competingScan(id);
        await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(() => session.SaveChangesAsync());

        await session.Events.FetchForWriting<Shipment>(id);

        if (!callAgain)
        {
            await Should.ThrowAsync<InvalidStreamOperationException>(() => session.SaveChangesAsync());
            return;
        }

        returned = await session.Events.ProjectLatestForCommit<Shipment>(id);

        if (appendMode == EventAppendMode.Rich)
        {
            await session.SaveChangesAsync();
            await shouldMatchTheCommit(returned, id);
        }
        else
        {
            // As without the call: under the quick modes a retry on the session of a failed commit fails again
            await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(() => session.SaveChangesAsync());
        }
    }
}
