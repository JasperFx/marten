#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.QueryHandlers;

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

    public ClosedShapeEventDocumentStorage(StoreOptions options): base(options)
    {
        var serializer = options.Serializer();

        _storage = Events.StreamIdentity == StreamIdentity.AsGuid
            ? EventStorageBuilder.Build<Guid>(Events, serializer)
            : EventStorageBuilder.Build<string>(Events, serializer);
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

    // Read-side: keep on codegen for v9. These two abstract methods on
    // EventDocumentStorage get emitted bodies that read event columns out
    // of a DbDataReader. The W4 (#4410) closed-shape hierarchy covers the
    // write path; the read selector is W5/Marten.SourceGenerator scope.
    //
    // The implementations below throw — meaning the closed-shape adapter
    // CANNOT yet replace the codegen path end-to-end. The wiring in
    // EventGraph.GeneratesCode.cs keeps the codegen-emitted class available
    // alongside this adapter and routes read-side calls there. Tracked as
    // open question (3) on #4410.

    public override void ApplyReaderDataToEvent(DbDataReader reader, IEvent e)
        => throw new NotImplementedException(
            "Read-side ApplyReaderDataToEvent is not yet covered by the closed-shape hierarchy. " +
            "Tracked as open question (3) on #4410 (W4 — Closed-shape event storage hierarchy).");

    public override Task ApplyReaderDataToEventAsync(DbDataReader reader, IEvent e, CancellationToken token)
        => throw new NotImplementedException(
            "Read-side ApplyReaderDataToEventAsync is not yet covered by the closed-shape hierarchy. " +
            "Tracked as open question (3) on #4410 (W4 — Closed-shape event storage hierarchy).");
}
