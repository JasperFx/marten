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
/// W3 spike (M1): <see cref="IDocumentMetadataBinder{TDoc}"/> for the
/// <c>mt_last_modified</c> column. Server-side value — declares
/// <see cref="ValueSql"/> as <c>transaction_timestamp()</c> and skips
/// <see cref="BindParameter"/>. The descriptor's SQL prefix bakes the
/// literal into the VALUES list so no parameter slot is reserved.
/// </summary>
internal sealed class DocumentLastModifiedBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    private readonly Action<TDoc, DateTimeOffset>? _setter;

    public DocumentLastModifiedBinder(MemberInfo? lastModifiedMember)
    {
        if (lastModifiedMember is not null)
        {
            _setter = LambdaBuilder.Setter<TDoc, DateTimeOffset>(lastModifiedMember);
        }
    }

    public string ColumnName => Marten.Schema.SchemaConstants.LastModifiedColumn;

    public string ValueSql => "transaction_timestamp()";

    public void BindParameter(DbParameter parameter, TDoc document, IStorageSession session)
    {
        // No-op — IsServerSide is true; the operation skips this binder
        // in its write loop. The SQL literal in ValueSql does the work.
    }

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IStorageSession session)
    {
        if (_setter is null) return;
        if (reader.IsDBNull(columnOrdinal)) return;

        var ts = reader.GetFieldValue<DateTimeOffset>(columnOrdinal);
        _setter(document, ts);
    }

    public BulkColumnValue GetBulkValue(TDoc document)
    {
        // COPY can't run transaction_timestamp() — compute client-side.
        // Slight skew vs. SQL writes that get the transaction's
        // commit time; acceptable for the bulk path.
        var now = DateTimeOffset.UtcNow;
        _setter?.Invoke(document, now);
        return new BulkColumnValue(now, StorageColumnType.Timestamp);
    }
}
