using Marten.Storage.Metadata;

namespace Marten.Schema;

public class DocumentMetadataCollection
{
    public DocumentMetadataCollection(DocumentMapping parent)
    {
        DocumentType = new DocumentTypeColumn(parent);
    }

    public MetadataColumn Version { get; } = new VersionColumn();
    public MetadataColumn Revision { get; } = new RevisionColumn();
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
