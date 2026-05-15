using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.EventStorage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// Verifies the closed-shape read-side port (#4411 — W4) reads back exactly
/// what the codegen path produces. The closed-shape adapter
/// (<see cref="ClosedShapeEventDocumentStorage"/>) walks
/// <c>RichEventStorageDescriptor.ReaderColumns</c> and dispatches per column
/// via <c>IEventTableColumn.ReadValueSync</c> / <c>ReadValueAsync</c> — this
/// test asserts the output matches the codegen-emitted
/// <c>ApplyReaderDataToEvent</c> body for the same row.
/// </summary>
/// <remarks>
/// <para>
/// We can't yet flip <c>UseClosedShapeStorage</c> on end-to-end because
/// the closed-shape write path is still stubbed (#4413 / #4414). So the
/// test uses the codegen path to <i>append</i> events, then constructs a
/// <see cref="ClosedShapeEventDocumentStorage"/> directly and runs the same
/// select-from-mt_events query against it, comparing the resolved
/// <c>IEvent</c>s field-by-field.
/// </para>
/// </remarks>
public class Bug_4411_closed_shape_read_side : OneOffConfigurationsContext
{
    [Fact]
    public async Task closed_shape_reads_match_codegen_reads_for_scalar_metadata_config()
    {
        // Exercises every read-back column except Headers — Headers needs
        // ISerializer threading that isn't yet on the IEventTableColumn read
        // surface (see HeadersColumn comment + follow-up of #4411). The
        // non-optional event columns (id, stream_id, version, timestamp,
        // is_archived, seq_id, tenant_id) are always exercised.
        StoreOptions(opts =>
        {
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.UserNameEnabled = true;
        });

        var streamId = Guid.NewGuid();
        const string causation = "cause-4411";
        const string correlation = "corr-4411";
        const string user = "tester";

        await using (var session = theStore.LightweightSession())
        {
            session.CausationId = causation;
            session.CorrelationId = correlation;
            session.LastModifiedBy = user;

            session.Events.StartStream(streamId,
                new QuestStarted { Name = "Closed-shape read-side check" },
                new MembersJoined { Members = new[] { "Frodo", "Sam" } });
            await session.SaveChangesAsync();
        }

        // 1) Read back via the codegen path — these are the "expected" values.
        IEvent[] codegenEvents;
        await using (var query = theStore.QuerySession())
        {
            codegenEvents = (await query.Events.FetchStreamAsync(streamId)).ToArray();
        }

        codegenEvents.Length.ShouldBe(2);

        // 2) Read back via the closed-shape adapter. Constructed directly
        //    against the same StoreOptions; only the read methods are
        //    exercised (write path is still stubbed in v9-alpha).
        var adapter = new ClosedShapeEventDocumentStorage(theStore.Options);
        var fields = adapter.SelectFields().Join(", ");
        var sql = $"select {fields} from {theStore.Options.DatabaseSchemaName}.mt_events where stream_id = :stream order by version";

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("stream", streamId);

        await using var reader = await cmd.ExecuteReaderAsync();
        var closedShapeEvents = new System.Collections.Generic.List<IEvent>();
        while (await reader.ReadAsync())
        {
            closedShapeEvents.Add(await adapter.ResolveAsync(reader, CancellationToken.None));
        }

        closedShapeEvents.Count.ShouldBe(codegenEvents.Length);

        // 3) Field-by-field equivalence — proves every wired
        //    IEventTableColumn.ReadValueSync/Async matches the codegen body.
        for (var i = 0; i < codegenEvents.Length; i++)
        {
            var expected = codegenEvents[i];
            var actual = closedShapeEvents[i];

            actual.Id.ShouldBe(expected.Id);
            actual.StreamId.ShouldBe(expected.StreamId);
            actual.Version.ShouldBe(expected.Version);
            actual.Sequence.ShouldBe(expected.Sequence);
            actual.Timestamp.ShouldBe(expected.Timestamp);
            actual.TenantId.ShouldBe(expected.TenantId);
            actual.IsArchived.ShouldBe(expected.IsArchived);
            actual.CausationId.ShouldBe(causation);
            actual.CorrelationId.ShouldBe(correlation);
            actual.UserName.ShouldBe(user);
            actual.EventType.ShouldBe(expected.EventType);
            actual.EventTypeName.ShouldBe(expected.EventTypeName);
        }
    }

    [Fact]
    public async Task closed_shape_sync_read_matches_codegen_for_default_metadata()
    {
        // Default metadata config — no optional metadata columns. Exercises
        // the sync path on the always-on columns: id / stream_id / version /
        // timestamp / tenant_id / mt_dotnet_type / seq_id / is_archived.
        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new QuestStarted { Name = "Default-config read" });
            await session.SaveChangesAsync();
        }

        IEvent expected;
        await using (var query = theStore.QuerySession())
        {
            expected = (await query.Events.FetchStreamAsync(streamId)).Single();
        }

        var adapter = new ClosedShapeEventDocumentStorage(theStore.Options);
        var fields = adapter.SelectFields().Join(", ");
        var sql = $"select {fields} from {theStore.Options.DatabaseSchemaName}.mt_events where stream_id = :stream";

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("stream", streamId);

        await using var reader = await cmd.ExecuteReaderAsync();
        reader.Read();
        var actual = adapter.Resolve(reader);

        actual.Id.ShouldBe(expected.Id);
        actual.StreamId.ShouldBe(expected.StreamId);
        actual.Version.ShouldBe(expected.Version);
        actual.Sequence.ShouldBe(expected.Sequence);
        actual.Timestamp.ShouldBe(expected.Timestamp);
        actual.TenantId.ShouldBe(expected.TenantId);
        actual.IsArchived.ShouldBeFalse();
    }
}
