#nullable enable
using Marten.Schema;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M1): per-mapping descriptor that the closed-shape document
/// storage class composes with at construction. Holds the pre-built SQL
/// strings + the ordered metadata-binder arrays. Built once per
/// <see cref="DocumentMapping"/>; held as a <c>readonly</c> field on the
/// storage instance. Closed-shape equivalent of <c>RichEventStorageDescriptor</c>
/// from W4.
/// </summary>
public sealed class DocumentStorageDescriptor<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    public DocumentStorageDescriptor(
        IIdentification<TDoc, TId> identification,
        IDocumentMetadataBinder<TDoc>[] clientSideWriteBinders,
        IDocumentMetadataBinder<TDoc>[] readBinders,
        string upsertSql,
        int dataColumnIndex)
    {
        Identification = identification;
        ClientSideWriteBinders = clientSideWriteBinders;
        ReadBinders = readBinders;
        UpsertSql = upsertSql;
        DataColumnIndex = dataColumnIndex;
    }

    public IIdentification<TDoc, TId> Identification { get; }

    /// <summary>
    /// Subset of the metadata binders that consume a <c>?</c> parameter
    /// slot in the VALUES list. Server-side binders (e.g.
    /// <c>transaction_timestamp()</c> for <c>mt_last_modified</c>) are
    /// excluded — their literal SQL is baked into <see cref="UpsertSql"/>.
    /// </summary>
    public IDocumentMetadataBinder<TDoc>[] ClientSideWriteBinders { get; }

    /// <summary>
    /// All metadata binders applied on the read path, in column order
    /// starting at <see cref="DataColumnIndex"/> + 1. Includes both
    /// client-side and server-side binders — on read they're symmetric
    /// (each binder consumes one result column).
    /// </summary>
    public IDocumentMetadataBinder<TDoc>[] ReadBinders { get; }

    /// <summary>
    /// Full upsert SQL with <c>?</c> placeholders for client-side
    /// parameters and inline literals for server-side ones — e.g.
    /// <c>"insert into schema.mt_doc_foo (id, data, mt_version, mt_dotnet_type, mt_last_modified) values (?, ?, ?, ?, transaction_timestamp()) on conflict (id) do update set …"</c>.
    /// Fed to <c>ICommandBuilder.AppendWithParameters(sql, '?')</c> at
    /// write time; the returned <c>NpgsqlParameter[]</c> is filled by the
    /// operation in order: id, data, then each
    /// <see cref="ClientSideWriteBinders"/> entry.
    /// </summary>
    public string UpsertSql { get; }

    /// <summary>
    /// Position of the <c>data</c> column in the SELECT projection. For
    /// Lightweight style the column order is <c>id, data, ...metadata</c>,
    /// so <see cref="DataColumnIndex"/> is 1 and metadata starts at 2.
    /// </summary>
    public int DataColumnIndex { get; }
}
