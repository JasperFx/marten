using Marten.Internal;
using Marten.Services;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Protected;

internal static class StoredEventPayload
{
    private static readonly byte[] EmptyJsonObject = "{}"u8.ToArray();

    /// <summary>
    ///     Appends the assignments that store <paramref name="data" /> the way an append stores it today:
    ///     <c>type</c>, <c>mt_dotnet_type</c>, <c>data</c> and <c>bdata</c>. All four are always set, because a
    ///     row left with its old <c>bdata</c> is read from <c>bdata</c>, and one left under an upcaster's source
    ///     name is upcast a second time.
    /// </summary>
    public static void AppendAssignments(ICommandBuilder builder, IStorageSession session, EventMapping mapping,
        object data)
    {
        builder.Append("type = ");
        builder.AppendParameter(mapping.EventTypeName, NpgsqlDbType.Varchar);
        builder.Append(", mt_dotnet_type = ");
        builder.AppendParameter(mapping.DotNetTypeName, NpgsqlDbType.Varchar);
        builder.Append(", data = ");

        if (mapping.IsBinary)
        {
            builder.AppendParameter(EmptyJsonObject, NpgsqlDbType.Jsonb);
            builder.Append(", bdata = ");
            builder.AppendParameter(mapping.BinarySerializer!.Serialize(mapping.DocumentType, data), NpgsqlDbType.Bytea);
            return;
        }

        using (var buffer = new PooledByteBufferWriter())
        {
            session.Serializer.WriteTo(buffer, data);
            builder.AppendParameter(buffer.ToSizedArray(), NpgsqlDbType.Jsonb);
        }

        builder.Append(", bdata = null");
    }
}
