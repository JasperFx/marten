using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Exceptions;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// #4617 section 5 / #4611 — single-DB conjoined variant of the regression
/// already pinned for sharded in
/// <c>Sharded/sharded_tenancy_per_tenant_events.cs::starting_then_appending_a_stream_works_with_mandatory_stream_type</c>.
///
/// <para>
/// Before #4613: <c>StartStream</c> under
/// <c>UseTenantPartitionedEvents</c> + <c>UseMandatoryStreamTypeDeclaration</c>
/// routed through the bulk <c>mt_quick_append_events</c> function and tripped
/// the post-process guard (first event version == 1) which incorrectly
/// rejected a legitimate StartStream as "appending to a non-existent stream".
/// Events were tombstoned, the <c>mt_streams</c> row never landed, and a
/// subsequent Append against the (never-created) stream blew up with
/// <see cref="NonExistentStreamException"/>.
/// </para>
///
/// <para>
/// The fix exempted <c>StreamActionType.Start</c> from the post-process guard.
/// This single-DB conjoined variant pins the fix for the non-sharded shape;
/// the sharded variant already lives in the migrated test file. Each test uses
/// its own DocumentStore (NOT the shared fixture) because
/// <c>UseMandatoryStreamTypeDeclaration</c> is a store-wide flag that would
/// affect every sibling test on a shared fixture.
/// </para>
/// </summary>
public class Bug_4611_mandatory_stream_type_under_partitioning : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_4611_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(_schema); } catch { }

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseMandatoryStreamTypeDeclaration = true;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Events.AddEventType<MandatoryEvent>();
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task StartStream_with_aggregate_type_followed_by_Append_works()
    {
        // The headline #4611 regression — Start then Append must both succeed,
        // and the mt_streams row's type column must carry the AggregateType name.
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha");

        var streamId = Guid.NewGuid();
        await using (var session = _store.LightweightSession("alpha"))
        {
            session.Events.StartStream<MandatoryAggregate>(streamId, new MandatoryEvent("first"));
            await session.SaveChangesAsync();
        }

        await using (var query = _store.QuerySession("alpha"))
        {
            (await query.Events.FetchStreamAsync(streamId)).Count.ShouldBe(1);
        }

        await using (var session = _store.LightweightSession("alpha"))
        {
            session.Events.Append(streamId, new MandatoryEvent("second"));
            await session.SaveChangesAsync();
        }

        await using (var query = _store.QuerySession("alpha"))
        {
            (await query.Events.FetchStreamAsync(streamId)).Count.ShouldBe(2);
        }

        // The end state pin: mt_streams row exists with type = aggregate type alias.
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand($"select type from {_schema}.mt_streams where id = @id and tenant_id = @tid");
        cmd.Parameters.AddWithValue("id", streamId);
        cmd.Parameters.AddWithValue("tid", "alpha");
        var typeName = (string?)await cmd.ExecuteScalarAsync();
        typeName.ShouldNotBeNull("mt_streams row must exist (not tombstoned) — the headline #4611 regression");
        typeName.ShouldBe(_store.Events.AggregateAliasFor(typeof(MandatoryAggregate)));
    }

    [Fact]
    public async Task untyped_StartStream_still_throws_StreamTypeMissingException()
    {
        // The companion #4611 pin: the API-level guard in EventStore.StartStream
        // STILL fires synchronously for the no-type overload when
        // UseMandatoryStreamTypeDeclaration is on. Pre-#4613's fix the
        // bulk-path post-process guard was the wrong layer; the correct guard
        // (this one) was never the problem.
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha");

        await using var session = _store.LightweightSession("alpha");

        Should.Throw<StreamTypeMissingException>(() =>
        {
            // No-type StartStream overload — must throw at call-site, not at
            // SaveChangesAsync.
            session.Events.StartStream(Guid.NewGuid(), new MandatoryEvent("should-not-land"));
        });
    }
}

public record MandatoryEvent(string Label);

public class MandatoryAggregate
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public void Apply(MandatoryEvent e) => Label = e.Label;
}
