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
using Marten.EventStorage.Querying;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.QueryHandlers;
using Marten.Services;

namespace Marten.EventStorage;

/// <summary>
/// Non-codegen <see cref="EventDocumentStorage"/> subclass that delegates
/// the write-path methods to a closed-shape <see cref="EventStorage{TId}"/>
/// instance. Constructed by <see cref="EventGraph"/> as the sole event
/// storage adapter in v9.
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
/// Read-side <see cref="ApplyReaderDataToEvent"/> /
/// <see cref="ApplyReaderDataToEventAsync"/> walk
/// <see cref="IEventTableColumn.ReadValueSync"/> /
/// <see cref="IEventTableColumn.ReadValueAsync"/> over a column list
/// derived from <see cref="EventsTable.SelectColumns"/>; no codegen is
/// involved on either the read or the write path.
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

        // #4821 event E3: the storage hierarchy + builder now live in Weasel.Storage and the
        // builder is dialect-agnostic — Marten supplies the Postgres dialect here (the same
        // Marten-resident PostgresEventStoreDialect the builder used to construct internally).
        var dialect = new Dialects.PostgresEventStoreDialect();
        _storage = Events.StreamIdentity == StreamIdentity.AsGuid
            ? EventStorageBuilder.Build<Guid>(dialect, Events.AppendMode, Events, _serializer)
            : EventStorageBuilder.Build<string>(dialect, Events.AppendMode, Events, _serializer);

        // Read-side column list for ApplyReaderDataToEvent (#4411). Built off
        // EventsTable.SelectColumns() with positions 0/1/2/3 stripped:
        // - 0/1/2 (data/type/mt_dotnet_type) are read by the base ISelector<IEvent>.
        // - 3 (bdata, #4515) is read inline in EventDocumentStorage.Resolve to
        //   pick the JSON-vs-binary deserialization path; the bdata column's
        //   own ReadValueSync is a no-op so including it here would be wasted.
        // Identical across all three append-mode variants (read shape doesn't
        // depend on write shape) so we build it here instead of routing
        // through EventStorage<TId>.
        _readerColumns = new Marten.Events.Schema.EventsTable(Events)
            .SelectColumns()
            .Skip(4)
            .ToArray();
    }

    /// <summary>
    /// Helper that resolves <see cref="_storage"/> to its strongly-typed
    /// shape based on <see cref="EventGraph.StreamIdentity"/>. Inlined as
    /// two property reads since the identity choice is fixed at
    /// construction.
    /// </summary>
    // Fully-qualified: this file's namespace is `Marten.EventStorage`, whose trailing segment
    // shadows the simple name `EventStorage` (the type now lives in Weasel.Storage after E3).
    private Weasel.Storage.EventStorage<Guid> GuidStorage => (Weasel.Storage.EventStorage<Guid>)_storage;
    private Weasel.Storage.EventStorage<string> StringStorage => (Weasel.Storage.EventStorage<string>)_storage;

    public override Weasel.Storage.IStorageOperation AppendEvent(EventGraph events, IStorageSession session, StreamAction stream, IEvent e)
    {
        return Events.StreamIdentity == StreamIdentity.AsGuid
            ? GuidStorage.AppendEvent(session, stream, e)
            : StringStorage.AppendEvent(session, stream, e);
    }

    public override Weasel.Storage.IStorageOperation QuickAppendEventWithVersion(StreamAction stream, IEvent e)
    {
        return Events.StreamIdentity == StreamIdentity.AsGuid
            ? GuidStorage.QuickAppendEventWithVersion(stream, e)
            : StringStorage.QuickAppendEventWithVersion(stream, e);
    }

    public override Weasel.Storage.IStorageOperation QuickAppendEvents(StreamAction stream)
    {
        return Events.StreamIdentity == StreamIdentity.AsGuid
            ? GuidStorage.QuickAppendEvents(stream)
            : StringStorage.QuickAppendEvents(stream);
    }

    public override Weasel.Storage.IStorageOperation InsertStream(StreamAction stream)
    {
        return Events.StreamIdentity == StreamIdentity.AsGuid
            ? GuidStorage.InsertStream(stream)
            : StringStorage.InsertStream(stream);
    }

    public override Weasel.Storage.IStorageOperation UpdateStreamVersion(StreamAction stream)
    {
        return Events.StreamIdentity == StreamIdentity.AsGuid
            ? GuidStorage.UpdateStreamVersion(stream)
            : StringStorage.UpdateStreamVersion(stream);
    }

    public override Weasel.Storage.IStorageOperation AssertStreamVersion(StreamAction stream)
    {
        return Events.StreamIdentity == StreamIdentity.AsGuid
            ? GuidStorage.AssertStreamVersion(stream)
            : StringStorage.AssertStreamVersion(stream);
    }

    public override IQueryHandler<StreamState> QueryForStream(StreamAction stream)
    {
        // Implemented here rather than on EventStorage<TId> (E0 pre-work for
        // the Weasel.Storage move): the stream-state lookup rides Marten's
        // query-handler pipeline (IQueryHandler<T> / StreamStateQueryHandler),
        // which stays Marten-local — and the implementation was identical
        // across all three append-mode storages anyway. Keeping it on the
        // adapter leaves the movable hierarchy purely write-side.
        var tenantId = Events.TenancyStyle == TenancyStyle.Conjoined ? stream.TenantId : null;

        return Events.StreamIdentity == StreamIdentity.AsGuid
            ? new ClosedShapeStreamStateQueryHandler<Guid>(StreamStateSelectSql, stream.Id, tenantId)
            : new ClosedShapeStreamStateQueryHandler<string>(StreamStateSelectSql, stream.Key!, tenantId);
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
            // Offset is + 4: 0/1/2 are data/type/mt_dotnet_type (base
            // ISelector<IEvent>); 3 is bdata (#4515 — consumed inline in
            // Resolve to pick the deserialization path).
            _readerColumns[i].ReadValueSync(reader, i + 4, e, _serializer);
        }
    }

    public override async Task ApplyReaderDataToEventAsync(DbDataReader reader, IEvent e, CancellationToken token)
    {
        for (var i = 0; i < _readerColumns.Count; i++)
        {
            await _readerColumns[i]
                .ReadValueAsync(reader, i + 4, e, _serializer, token)
                .ConfigureAwait(false);
        }
    }
}
