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
        // Mirrors the codegen path's emitted shape for the headers column:
        //
        //     var parameter = parameterBuilder.AppendParameter<object>(DBNull.Value);
        //     session.Serializer.WriteToParameter(parameter, evt.Headers);
        //
        // Using AppendParameter<object> (not AppendParameter<DBNull> via the
        // raw DBNull static type) is important — Npgsql refuses to serialize
        // DBNull as a typed parameter, but accepts object + DBNull.Value with
        // NpgsqlDbType.Jsonb as a NULL jsonb. WriteToParameter then either
        // writes the actual JSON bytes (skipping intermediate string
        // allocation via ISerializer.WriteTo) or leaves the parameter at
        // DBNull when Headers is null.
        var parameter = pb.AppendParameter<object>(System.DBNull.Value);
        parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
        session.Serializer.WriteToParameter(parameter, @event.Headers);
    }

    // OnRead — default no-op (inherited from the interface default). Headers
    // are write-only; nothing comes back from the server for this column.
}
