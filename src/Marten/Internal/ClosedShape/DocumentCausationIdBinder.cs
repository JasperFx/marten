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
/// <see cref="IDocumentMetadataBinder{TDoc}"/> for the <c>causation_id</c>
/// column. Value comes from <see cref="IStorageSession.CausationId"/> on
/// every write; read path projects the stored value onto the document's
/// <c>[CausationId]</c>-annotated member when one exists.
/// </summary>
internal sealed class DocumentCausationIdBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    private readonly Action<TDoc, string?>? _setter;

    public DocumentCausationIdBinder(MemberInfo? causationIdMember)
    {
        if (causationIdMember is not null)
        {
            _setter = LambdaBuilder.Setter<TDoc, string?>(causationIdMember);
        }
    }

    public string ColumnName => CausationIdColumn.ColumnName;

    public string ValueSql => "?";

    public void BindParameter(NpgsqlParameter parameter, TDoc document, IStorageSession session)
    {
        parameter.NpgsqlDbType = NpgsqlDbType.Varchar;
        parameter.Value = (object?)session.CausationId ?? DBNull.Value;
    }

    public void ApplyToDocument(TDoc document, IStorageSession session)
        => _setter?.Invoke(document, session.CausationId);

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IStorageSession session)
    {
        if (_setter is null) return;
        if (reader.IsDBNull(columnOrdinal)) return;

        var value = reader.GetFieldValue<string>(columnOrdinal);
        _setter(document, value);
    }

    public BulkColumnValue GetBulkValue(TDoc document) => BulkColumnValue.Null;
}
