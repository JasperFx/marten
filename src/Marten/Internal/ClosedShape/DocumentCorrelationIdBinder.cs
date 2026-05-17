#nullable enable
using System;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Storage.Metadata;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <see cref="IDocumentMetadataBinder{TDoc}"/> for the
/// <c>correlation_id</c> column. The value comes from
/// <see cref="IMartenSession.CorrelationId"/> on every write — not from
/// the document — mirroring the codegen path's
/// <c>setStringParameter(_, session.CorrelationId)</c> emit. On read the
/// stored value is projected back onto the document's
/// <c>[CorrelationId]</c>-annotated member when one exists.
/// </summary>
internal sealed class DocumentCorrelationIdBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    private readonly Action<TDoc, string?>? _setter;

    public DocumentCorrelationIdBinder(MemberInfo? correlationIdMember)
    {
        if (correlationIdMember is not null)
        {
            _setter = LambdaBuilder.Setter<TDoc, string?>(correlationIdMember);
        }
    }

    public string ColumnName => CorrelationIdColumn.ColumnName;

    public string ValueSql => "?";

    public void BindParameter(NpgsqlParameter parameter, TDoc document, IMartenSession session)
    {
        parameter.NpgsqlDbType = NpgsqlDbType.Varchar;
        parameter.Value = (object?)session.CorrelationId ?? DBNull.Value;
    }

    public void ApplyToDocument(TDoc document, IMartenSession session)
        => _setter?.Invoke(document, session.CorrelationId);

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IMartenSession session)
    {
        if (_setter is null) return;
        if (reader.IsDBNull(columnOrdinal)) return;

        var value = reader.GetFieldValue<string>(columnOrdinal);
        _setter(document, value);
    }

    public Task WriteToBulkAsync(NpgsqlBinaryImporter writer, TDoc document,
        ISerializer serializer, CancellationToken cancellation)
    {
        // Bulk loader has no session — no source for a correlation id.
        // Write null; mirrors the codegen path's bulk emit which writes a
        // constant placeholder (the column was never session-aware on the
        // COPY path).
        return writer.WriteNullAsync(cancellation);
    }
}
