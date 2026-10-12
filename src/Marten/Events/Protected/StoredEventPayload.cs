using Marten.Internal;
using Marten.Services;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Protected;

internal static class StoredEventPayload
{
    private static readonly byte[] EmptyJsonObject = "{}"u8.ToArray();

    /// <summary>
    ///     Appends <c>data = ?, bdata = ?</c> with the payload stored the way an append stores it today. Both
    ///     columns are always set: a row whose <c>bdata</c> is left behind is read from <c>bdata</c>.
    /// </summary>
    public static void AppendAssignments(ICommandBuilder builder, IStorageSession session, EventMapping mapping,
        object data)
    {
        builder.Append("data = ");

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
