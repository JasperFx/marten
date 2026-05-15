#nullable enable
using JasperFx.Events;
using Marten.Internal;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.EventStorage.Metadata;

/// <summary>
/// Sample <see cref="IEventMetadataBinder"/> for the optional <c>headers</c>
/// JSONB column. Write-only — no read-back. The descriptor builder includes
/// this binder in the array iff the consumer's <c>StoreOptions.Events</c>
/// has headers enabled.
/// </summary>
/// <remarks>
/// Demonstrates the "configurable presence" axis: the binder either is or
/// isn't in the descriptor's array. The closed-shape source-gen-emitted
/// operation doesn't need any if/then for it — if headers aren't enabled,
/// the binder isn't in the array and the loop iteration count goes down by
/// one. No per-call cost when the feature is off.
/// </remarks>
internal sealed class HeadersColumnBinder: IEventMetadataBinder
{
    public string ColumnName => "headers";

    public string ValueSql => "?";

    public void Bind(IGroupedParameterBuilder pb, StreamAction stream, IEvent @event, IMartenSession session)
    {
        // Reads from IEvent.Headers (Dictionary<string,object>?), serialized
        // by the session's JSON serializer. Today's codegen path does the same
        // thing inside the emitted ConfigureCommand body.
        var headers = @event.Headers;
        if (headers == null || headers.Count == 0)
        {
            pb.AppendParameter(System.DBNull.Value, NpgsqlDbType.Jsonb);
            return;
        }

        var json = session.Serializer.ToJson(headers);
        pb.AppendParameter(json, NpgsqlDbType.Jsonb);
    }

    // OnRead — default no-op (inherited from the interface default). Headers
    // are write-only; nothing comes back from the server for this column.
}
