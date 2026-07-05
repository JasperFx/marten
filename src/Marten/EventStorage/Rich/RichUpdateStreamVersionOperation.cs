#nullable enable
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.EventStorage.Rich;

/// <summary>
/// Closed-shape <see cref="UpdateStreamVersion"/> for the Rich (full-mode)
/// path. Like <see cref="RichInsertStreamOperation"/> the command shape
/// lives on a descriptor-owned closure
/// (<see cref="RichEventStorageDescriptor.ConfigureUpdateStreamVersionCommand"/>)
/// — variation by stream-identity + tenancy is resolved once at startup,
/// not on each call.
/// </summary>
/// <remarks>
/// SQL shape (handled by the closure):
/// <c>update {schema}.mt_streams set version = $1 where id = $2 and version = $3 [and tenant_id = $4] returning version</c>.
/// The base class's <c>Postprocess</c> throws
/// <see cref="Marten.Exceptions.EventStreamUnexpectedMaxEventIdException"/>
/// when the row count is zero (expected version mismatch).
/// </remarks>
internal sealed class RichUpdateStreamVersionOperation: UpdateStreamVersion
{
    private readonly RichEventStorageDescriptor _descriptor;

    public RichUpdateStreamVersionOperation(RichEventStorageDescriptor descriptor, StreamAction stream): base(stream)
    {
        _descriptor = descriptor;
    }

    public override void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        _descriptor.ConfigureUpdateStreamVersionCommand(builder, Stream);
    }
}
