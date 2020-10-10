using Marten.Storage;

namespace Marten.Schema
{
    public class DocumentMetadataCollection
    {
        public DocumentMetadataCollection(DocumentMapping parent)
        {
            DocumentType = new DocumentTypeColumn(parent);
        }

        public MetadataColumn Version { get; } = new VersionColumn();
        public MetadataColumn LastModified { get; } = new LastModifiedColumn();
        public MetadataColumn TenantId { get; } = new TenantIdColumn();
        public MetadataColumn IsSoftDeleted { get; } = new SoftDeletedColumn();
        public MetadataColumn SoftDeletedAt { get; } = new DeletedAtColumn();
        public MetadataColumn DocumentType { get; }

        public MetadataColumn DotNetType { get; } = new DotNetTypeColumn();

    }
}
