using Marten.Storage;
using Marten.Storage.Metadata;

namespace Marten.Schema;

public class DocumentMetadataCollection
{
    public DocumentMetadataCollection(DocumentMapping parent)
    {
        DocumentType = new DocumentTypeColumn(parent);

        // #6144: causation id, correlation id, headers and last modified by are off by default, as in Marten 9.
        // The 10.0 alphas turned them on (#5812); the rc.1 perf round measured them as about half the remaining
        // projection rebuild cost, and they added four columns to every document table on upgrade. Policies,
        // attributes and Schema.For<T>() turn them on.
    }

    public MetadataColumn Version { get; } = new VersionColumn();

    // #4614: VersionedPolicy reassigns this to RevisionColumnInt32 when the document
    // type implements IRevisioned (int) so the mt_version column is `integer` rather
    // than `bigint`, restoring the Marten 8 schema shape. ILongVersioned (long) docs
    // keep the default RevisionColumn (bigint). The setter is internal because the
    // variant choice is policy-driven, not user-driven.
    public MetadataColumn Revision { get; internal set; } = new RevisionColumn();
    public MetadataColumn LastModified { get; } = new LastModifiedColumn();
    public MetadataColumn CreatedAt { get; } = new CreatedAtColumn();
    public MetadataColumn TenantId { get; } = new TenantIdColumn();
    public MetadataColumn IsSoftDeleted { get; } = new SoftDeletedColumn();
    public MetadataColumn SoftDeletedAt { get; } = new DeletedAtColumn();
    public MetadataColumn DocumentType { get; }

    public MetadataColumn DotNetType { get; } = new DotNetTypeColumn();

    public MetadataColumn CausationId { get; } = new CausationIdColumn();
    public MetadataColumn CorrelationId { get; } = new CorrelationIdColumn();
    public MetadataColumn LastModifiedBy { get; } = new LastModifiedByColumn();

    public MetadataColumn Headers { get; } = new HeadersColumn();
}
