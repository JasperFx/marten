#nullable enable
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.EventStorage.QuickWithServerTimestamps;

/// <summary>
/// Closed-shape <see cref="InsertStreamBase"/> for the QuickWithServerTimestamps
/// path. Symmetric with the Rich + Quick equivalents — delegates to a
/// descriptor-installed closure.
/// </summary>
internal sealed class QuickWithServerTimestampsInsertStreamOperation: InsertStreamBase
{
    private readonly QuickWithServerTimestampsEventStorageDescriptor _descriptor;

    public QuickWithServerTimestampsInsertStreamOperation(
        QuickWithServerTimestampsEventStorageDescriptor descriptor, StreamAction stream): base(stream)
    {
        _descriptor = descriptor;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        _descriptor.ConfigureInsertStreamCommand(builder, Stream);
    }
}
