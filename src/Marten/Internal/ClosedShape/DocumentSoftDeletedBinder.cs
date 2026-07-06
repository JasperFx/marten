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
/// <c>mt_deleted</c> column on a soft-delete mapping. Each write resets
/// the flag to <c>false</c> — saving a previously soft-deleted document
/// undeletes it. Matches the codegen path's
/// <c>UpsertFunction</c> behavior (the column appears in both the
/// INSERT VALUES list and the ON CONFLICT DO UPDATE SET clause).
/// </summary>
/// <remarks>
/// Read path: if the document declares an <c>[SoftDeleted]</c>-annotated
/// member, the loaded boolean is projected onto it. Otherwise the
/// column is in the table but absent from the SELECT projection.
/// </remarks>
internal sealed class DocumentSoftDeletedBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    private readonly Action<TDoc, bool>? _setter;

    public DocumentSoftDeletedBinder(MemberInfo? member)
    {
        if (member is not null)
        {
            _setter = LambdaBuilder.Setter<TDoc, bool>(member);
        }
    }

    public string ColumnName => Marten.Schema.SchemaConstants.DeletedColumn;

    public string ValueSql => "?";

    public void BindParameter(DbParameter parameter, TDoc document, IStorageSession session)
    {
        parameter.Value = false;
        ((NpgsqlParameter)parameter).NpgsqlDbType = NpgsqlDbType.Boolean;
    }

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IStorageSession session)
    {
        if (_setter is null) return;
        if (reader.IsDBNull(columnOrdinal)) return;

        var value = reader.GetFieldValue<bool>(columnOrdinal);
        _setter(document, value);
    }

    public BulkColumnValue GetBulkValue(TDoc document)
        => new(false, StorageColumnType.Boolean);
}
