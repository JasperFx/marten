#nullable enable
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.EventStorage.Quick;

/// <summary>
/// Closed-shape <see cref="InsertStreamBase"/> for the Quick-mode paths.
/// Identical shape to <c>Rich.RichInsertStreamOperation</c> — delegates to
/// a descriptor-installed closure built by the dialect. Same SQL shape too;
/// the divergence between Rich and Quick is on the per-event append, not
/// on stream lifecycle ops.
/// </summary>
internal sealed class QuickInsertStreamOperation: InsertStreamBase
{
    private readonly QuickEventStorageDescriptor _descriptor;

    public QuickInsertStreamOperation(QuickEventStorageDescriptor descriptor, StreamAction stream): base(stream)
    {
        _descriptor = descriptor;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        _descriptor.ConfigureInsertStreamCommand(builder, Stream);
    }
}
