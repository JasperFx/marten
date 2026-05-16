#nullable enable
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.EventStorage.QuickWithServerTimestamps;

/// <summary>
/// Closed-shape <see cref="UpdateStreamVersion"/> for the
/// QuickWithServerTimestamps path. Symmetric with the Rich + Quick
/// equivalents.
/// </summary>
internal sealed class QuickWithServerTimestampsUpdateStreamVersionOperation: UpdateStreamVersion
{
    private readonly QuickWithServerTimestampsEventStorageDescriptor _descriptor;

    public QuickWithServerTimestampsUpdateStreamVersionOperation(
        QuickWithServerTimestampsEventStorageDescriptor descriptor, StreamAction stream): base(stream)
    {
        _descriptor = descriptor;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        _descriptor.ConfigureUpdateStreamVersionCommand(builder, Stream);
    }
}
