#nullable enable
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.EventStorage.Quick;

/// <summary>
/// Closed-shape <see cref="UpdateStreamVersion"/> for the Quick-mode paths.
/// Symmetric with <c>Rich.RichUpdateStreamVersionOperation</c>.
/// </summary>
internal sealed class QuickUpdateStreamVersionOperation: UpdateStreamVersion
{
    private readonly QuickEventStorageDescriptor _descriptor;

    public QuickUpdateStreamVersionOperation(QuickEventStorageDescriptor descriptor, StreamAction stream): base(stream)
    {
        _descriptor = descriptor;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        _descriptor.ConfigureUpdateStreamVersionCommand(builder, Stream);
    }
}
