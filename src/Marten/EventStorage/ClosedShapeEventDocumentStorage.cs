#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.QueryHandlers;
using Marten.Services;

namespace Marten.EventStorage;

/// <summary>
/// Non-codegen <see cref="EventDocumentStorage"/> subclass that delegates
/// the write-path methods to a closed-shape <see cref="EventStorage{TId}"/>
/// instance. Constructed by <see cref="EventGraph"/> when
/// <c>StoreOptions.Events.UseClosedShapeStorage</c> is on.
/// </summary>
/// <remarks>
/// <para>
/// Adapter — the existing Marten session code consumes
/// <see cref="EventDocumentStorage"/> as its event-storage surface. This
/// subclass overrides the per-method abstracts and routes each call into
/// the appropriate <see cref="EventStorage{TId}"/> factory method
/// (<c>AppendEvent</c> / <c>QuickAppendEvents</c> / etc.). The session
/// code itself doesn't have to know the closed-shape hierarchy exists.
/// </para>
/// <para>
/// Generic on identity is collapsed by picking either <see cref="Guid"/>
/// or <see cref="string"/> at construction. The runtime concrete type is
/// chosen via <see cref="EventGraph.StreamIdentity"/>.
/// </para>
/// <para>
/// <b>Read-side note:</b> <see cref="ApplyReaderDataToEvent"/> and
/// <see cref="ApplyReaderDataToEventAsync"/> are still codegen-emitted
/// in v9 even with the closed-shape flag on. The closed-shape work in
/// W4 covers the write path; the read-side equivalent lives with W5
/// (Marten.SourceGenerator) — the read-side method body needs per-event
/// type info that source-gen will provide. Until then, the codegen
/// path is still required for the read selector — see open question (3)
/// on #4410.
/// </para>
/// </remarks>
internal sealed class ClosedShapeEventDocumentStorage: EventDocumentStorage
{
    private readonly object _storage;
    private readonly IReadOnlyList<IEventTableColumn> _readerColumns;
    private readonly ISerializer _serializer;

    public ClosedShapeEventDocumentStorage(StoreOptions options): base(options)
    {
        _serializer = options.Serializer();

        _storage = Events.StreamIdentity == StreamIdentity.AsGuid
            ? EventStorageBuilder.Build<Guid>(Events, _serializer)
            : EventStorageBuilder.Build<string>(Events, _serializer);

        // Read-side column list for ApplyReaderDataToEvent (#4411). Built off
        // EventsTable.SelectColumns() with positions 0/1/2 stripped — those
        // are read by the base ISelector<IEvent>. Identical across all three
        // append-mode variants (read shape doesn't depend on write shape) so
        // we build it here instead of routing through EventStorage<TId>.
        _readerColumns = new Marten.Events.Schema.EventsTable(Events)
            .SelectColumns()
            .Skip(3)
            .ToArray();
    }

    /// <summary>
    /// Helper that resolves <see cref="_storage"/> to its strongly-typed
    /// shape based on <see cref="EventGraph.StreamIdentity"/>. Inlined as
    /// two property reads since the identity choice is fixed at
    /// construction.
    /// </summary>
    private EventStorage<Guid> GuidStorage => (EventStorage<Guid>)_storage;
    private EventStorage<string> StringStorage => (EventStorage<string>)_storage;

    public override IStorageOperation AppendEvent(EventGraph events, IMartenSession session, StreamAction stream, IEvent e)
    {
        return Events.StreamIdentity == StreamIdentity.AsGuid
            ? GuidStorage.AppendEvent(session, stream, e)
            : StringStorage.AppendEvent(session, stream, e);
    }

    public override IStorageOperation QuickAppendEventWithVersion(StreamAction stream, IEvent e)
    {
        return Events.StreamIdentity == StreamIdentity.AsGuid
            ? GuidStorage.QuickAppendEventWithVersion(stream, e)
            : StringStorage.QuickAppendEventWithVersion(stream, e);
    }

    public override IStorageOperation QuickAppendEvents(StreamAction stream)
    {
        return Events.StreamIdentity == StreamIdentity.AsGuid
            ? GuidStorage.QuickAppendEvents(stream)
            : StringStorage.QuickAppendEvents(stream);
    }

    public override IStorageOperation InsertStream(StreamAction stream)
    {
        return Events.StreamIdentity == StreamIdentity.AsGuid
            ? GuidStorage.InsertStream(stream)
            : StringStorage.InsertStream(stream);
    }

    public override IStorageOperation UpdateStreamVersion(StreamAction stream)
    {
        return Events.StreamIdentity == StreamIdentity.AsGuid
            ? GuidStorage.UpdateStreamVersion(stream)
            : StringStorage.UpdateStreamVersion(stream);
    }

    public override IQueryHandler<StreamState> QueryForStream(StreamAction stream)
    {
        return Events.StreamIdentity == StreamIdentity.AsGuid
            ? GuidStorage.QueryForStream(stream)
            : StringStorage.QueryForStream(stream);
    }

    // Read-side: closed-shape implementation (#4411). Iterates the descriptor's
    // reader-column list and dispatches per column via virtual
    // IEventTableColumn.ReadValueSync / Async. Each concrete column type
    // wraps a compiled-delegate reader (no per-row reflection, no boxing for
    // value-typed members) — see EventColumnReaders / EventTableColumn.
    //
    // Ordinal contract: columns 0/1/2 (data / type / mt_dotnet_type) are
    // read by the base ISelector<IEvent> in EventDocumentStorage; this loop
    // starts at ordinal 3. The dialect's BuildReaderColumns mirrors the
    // codegen path's `for (var i = 3; i < columns.Count; i++)` loop.

    public override void ApplyReaderDataToEvent(DbDataReader reader, IEvent e)
    {
        for (var i = 0; i < _readerColumns.Count; i++)
        {
            // Serializer-aware overload — most columns ignore the serializer
            // (default interface method falls through to the parameterless
            // ReadValueSync). HeadersColumn overrides this to deserialize
            // jsonb → Dictionary<string, object> via the session's serializer.
            _readerColumns[i].ReadValueSync(reader, i + 3, e, _serializer);
        }
    }

    public override async Task ApplyReaderDataToEventAsync(DbDataReader reader, IEvent e, CancellationToken token)
    {
        for (var i = 0; i < _readerColumns.Count; i++)
        {
            await _readerColumns[i]
                .ReadValueAsync(reader, i + 3, e, _serializer, token)
                .ConfigureAwait(false);
        }
    }
}
