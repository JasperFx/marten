#nullable enable
using System;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Storage.Metadata;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <see cref="IDocumentMetadataBinder{TDoc}"/> for the
/// <c>last_modified_by</c> column. Value comes from
/// <see cref="IStorageSession.LastModifiedBy"/> (alias of
/// <c>CurrentUserName</c>) on every write; read path projects the stored
/// value onto the document's <c>[LastModifiedBy]</c>-annotated member
/// when one exists.
/// </summary>
internal sealed class DocumentLastModifiedByBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    private readonly Action<TDoc, string?>? _setter;

    public DocumentLastModifiedByBinder(MemberInfo? lastModifiedByMember)
    {
        if (lastModifiedByMember is not null)
        {
            _setter = LambdaBuilder.Setter<TDoc, string?>(lastModifiedByMember);
        }
    }

    public string ColumnName => LastModifiedByColumn.ColumnName;

    public string ValueSql => "?";

    public void BindParameter(DbParameter parameter, TDoc document, IStorageSession session)
    {
        ((NpgsqlParameter)parameter).NpgsqlDbType = NpgsqlDbType.Varchar;
        // IMetadataContext.LastModifiedBy is the alias of CurrentUserName;
        // prefer CurrentUserName since the former is marked [Obsolete].
        parameter.Value = (object?)session.CurrentUserName ?? DBNull.Value;
    }

    public void ApplyToDocument(TDoc document, IStorageSession session)
        => _setter?.Invoke(document, session.CurrentUserName);

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IStorageSession session)
    {
        if (_setter is null) return;
        if (reader.IsDBNull(columnOrdinal)) return;

        var value = reader.GetFieldValue<string>(columnOrdinal);
        _setter(document, value);
    }

    public BulkColumnValue GetBulkValue(TDoc document) => BulkColumnValue.Null;
}
