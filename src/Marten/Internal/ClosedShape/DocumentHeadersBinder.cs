#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Internal.Sessions;
using Marten.Storage.Metadata;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <see cref="IDocumentMetadataBinder{TDoc}"/> for the <c>headers</c>
/// (jsonb) column. Write path pulls the headers off the session — under
/// <see cref="DocumentSessionBase"/> a per-session UTF-8 byte[] cache is
/// reused across the batch so N storage ops serialize the dictionary
/// once. Read path projects the deserialized dictionary onto the
/// document's <c>[Headers]</c>-annotated member when one exists.
/// </summary>
internal sealed class DocumentHeadersBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    private readonly Action<TDoc, Dictionary<string, object>?>? _setter;

    public DocumentHeadersBinder(MemberInfo? headersMember)
    {
        if (headersMember is not null)
        {
            _setter = LambdaBuilder.Setter<TDoc, Dictionary<string, object>?>(headersMember);
        }
    }

    public string ColumnName => HeadersColumn.ColumnName;

    public string ValueSql => "?";

    public void ApplyToDocument(TDoc document, IStorageSession session)
        => _setter?.Invoke(document, session.Headers);

    public void BindParameter(NpgsqlParameter parameter, TDoc document, IStorageSession session)
    {
        // Mirror the codegen path's setHeaderParameter: use the session's
        // cached UTF-8 byte[] when available (DocumentSessionBase
        // invalidates it on every SetHeader mutation so the cache stays
        // honest), otherwise fall back to direct serialization via
        // ISerializer.WriteToParameter.
        if (session is DocumentSessionBase docSession)
        {
            var cachedBytes = docSession.GetCachedSerializedHeaders();
            if (cachedBytes is null)
            {
                parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
                parameter.Value = DBNull.Value;
            }
            else
            {
                parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
                parameter.Value = cachedBytes;
            }
            return;
        }

        if (session.Headers is null)
        {
            parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
            parameter.Value = DBNull.Value;
        }
        else
        {
            session.Serializer.WriteToParameter(parameter, session.Headers);
        }
    }

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IStorageSession session)
    {
        if (_setter is null) return;
        if (reader.IsDBNull(columnOrdinal)) return;

        var headers = session.Serializer.FromJson<Dictionary<string, object>>(reader, columnOrdinal);
        _setter(document, headers);
    }

    public BulkColumnValue GetBulkValue(TDoc document)
        // Headers aren't carried on the COPY path — write a typed JSONB null, as before.
        => BulkColumnValue.TypedNull(StorageColumnType.Json);
}
