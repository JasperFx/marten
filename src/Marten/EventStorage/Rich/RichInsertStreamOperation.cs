#nullable enable
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.EventStorage.Rich;

/// <summary>
/// Closed-shape <see cref="InsertStreamBase"/> for the Rich (full-mode)
/// path. The complete command shape lives on
/// <see cref="RichEventStorageDescriptor.ConfigureInsertStreamCommand"/> —
/// a closure the dialect composes once at descriptor-build time based on
/// stream-identity (Guid vs string) and tenancy style. This class is the
/// minimal <see cref="IStorageOperation"/> shell: pull the closure off the
/// descriptor, invoke it.
/// </summary>
/// <remarks>
/// Why closure-on-descriptor rather than inlined-in-class: InsertStream
/// runs <i>once per stream</i> (not per event), so the delegate-dispatch
/// overhead is irrelevant compared to the round-trip latency. Source-gen
/// (W5) gets value mostly from the per-event AppendEvent hot path
/// (#4413 — <see cref="RichAppendEventOperation"/>), not from these
/// per-stream operations. Keeping the per-stream ops descriptor-driven
/// means the closed-shape matrix is bounded by stream-identity × tenancy
/// × strict-identity rather than expanding into hand-written subclasses.
/// </remarks>
internal sealed class RichInsertStreamOperation: InsertStreamBase
{
    private readonly RichEventStorageDescriptor _descriptor;

    public RichInsertStreamOperation(RichEventStorageDescriptor descriptor, StreamAction stream): base(stream)
    {
        _descriptor = descriptor;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        _descriptor.ConfigureInsertStreamCommand(builder, Stream);
    }
}
