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

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Read-only <see cref="IDocumentMetadataBinder{TDoc}"/> for the
/// <c>tenant_id</c> column. The write side is handled directly in the
/// storage operations (the tenant_id parameter is bound inline ahead of
/// the binder loop, since it's a primary key column with a fixed
/// position). This binder exists only so the read path can project the
/// column onto the document's <c>[TenantId]</c>-annotated member when
/// the mapping has one — added to <c>readBinders</c> only, never to
/// <c>writeBinders</c>.
/// </summary>
internal sealed class DocumentTenantIdBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    private readonly Action<TDoc, string?>? _setter;

    public DocumentTenantIdBinder(MemberInfo? tenantIdMember)
    {
        if (tenantIdMember is not null)
        {
            _setter = LambdaBuilder.Setter<TDoc, string?>(tenantIdMember);
        }
    }

    public string ColumnName => TenantIdColumn.Name;

    public string ValueSql => "?";

    public void BindParameter(NpgsqlParameter parameter, TDoc document, IMartenSession session)
        => throw new NotSupportedException(
            "tenant_id is bound directly by the storage operation; this binder is read-only.");

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IMartenSession session)
    {
        if (_setter is null) return;
        if (reader.IsDBNull(columnOrdinal)) return;

        var value = reader.GetFieldValue<string>(columnOrdinal);
        _setter(document, value);
    }

    public Task WriteToBulkAsync(NpgsqlBinaryImporter writer, TDoc document,
        ISerializer serializer, CancellationToken cancellation)
        => throw new NotSupportedException(
            "tenant_id is handled directly by the bulk loader; this binder is read-only.");
}
