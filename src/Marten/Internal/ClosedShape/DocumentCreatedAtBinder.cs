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

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Read-only <see cref="IDocumentMetadataBinder{TDoc}"/> for the
/// <c>mt_created_at</c> column. Issue #4575: in v8 the codegen storage path
/// projected the column back onto a <c>[CreatedAt]</c>-annotated /
/// <c>Metadata(m =&gt; m.CreatedAt.MapTo(...))</c> member; the v9 closed-shape
/// rewrite (#4498) ported every other metadata column but missed this one,
/// so the .NET member stayed at <c>default(DateTimeOffset)</c> after a
/// reload.
/// <para>
/// Unlike <see cref="DocumentLastModifiedBinder{TDoc}"/>, this binder is
/// added to <c>readBinders</c> only — never <c>writeBinders</c>. The
/// underlying <see cref="Marten.Storage.Metadata.CreatedAtColumn"/> carries
/// a <c>transaction_timestamp()</c> DEFAULT, so PostgreSQL fills the value
/// on insert; participating in <c>writeBinders</c> would put the column
/// into the UPDATE SET list too and clobber the original creation time on
/// every subsequent save.
/// </para>
/// </summary>
internal sealed class DocumentCreatedAtBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    private readonly Action<TDoc, DateTimeOffset>? _setter;

    public DocumentCreatedAtBinder(MemberInfo? createdAtMember)
    {
        if (createdAtMember is not null)
        {
            _setter = LambdaBuilder.Setter<TDoc, DateTimeOffset>(createdAtMember);
        }
    }

    public string ColumnName => Marten.Schema.SchemaConstants.CreatedAtColumn;

    // No write path — the column's transaction_timestamp() DEFAULT does
    // the work on insert, and CreatedAt is immutable after that. ValueSql
    // is non-"?" so IsServerSide is true; even if a future caller adds
    // this binder to writeBinders, BindParameter throwing keeps the
    // "never written client-side" invariant honest.
    public string ValueSql => "transaction_timestamp()";

    public void BindParameter(DbParameter parameter, TDoc document, IStorageSession session)
        => throw new NotSupportedException(
            "mt_created_at has a server-side DEFAULT and is never written through this binder.");

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IStorageSession session)
    {
        if (_setter is null) return;
        if (reader.IsDBNull(columnOrdinal)) return;

        var ts = reader.GetFieldValue<DateTimeOffset>(columnOrdinal);
        _setter(document, ts);
    }

    public BulkColumnValue GetBulkValue(TDoc document)
        => throw new NotSupportedException(
            "mt_created_at is filled by the column DEFAULT during bulk load; this binder is read-only.");
}
