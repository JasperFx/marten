#nullable enable
using System;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M9): <see cref="IDocumentMetadataBinder{TDoc}"/> for the
/// <c>mt_deleted_at</c> column on a soft-delete mapping. Each write
/// binds <see cref="DBNull"/> — only the soft-delete operation (issued
/// via the inherited <c>DeleteFragment</c>) writes a concrete timestamp.
/// Saving a previously soft-deleted document clears the timestamp,
/// matching <c>UpsertFunction</c> codegen.
/// </summary>
internal sealed class DocumentSoftDeletedAtBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    private readonly Action<TDoc, DateTimeOffset?>? _setter;

    public DocumentSoftDeletedAtBinder(MemberInfo? member)
    {
        if (member is not null)
        {
            _setter = LambdaBuilder.Setter<TDoc, DateTimeOffset?>(member);
        }
    }

    public string ColumnName => Marten.Schema.SchemaConstants.DeletedAtColumn;

    public string ValueSql => "?";

    public void BindParameter(NpgsqlParameter parameter, TDoc document, IStorageSession session)
    {
        parameter.Value = DBNull.Value;
        parameter.NpgsqlDbType = NpgsqlDbType.TimestampTz;
    }

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IStorageSession session)
    {
        if (_setter is null) return;
        if (reader.IsDBNull(columnOrdinal))
        {
            _setter(document, null);
            return;
        }

        var value = reader.GetFieldValue<DateTimeOffset>(columnOrdinal);
        _setter(document, value);
    }

    public BulkColumnValue GetBulkValue(TDoc document) => BulkColumnValue.Null;
}
