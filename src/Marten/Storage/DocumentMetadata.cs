using System;

namespace Marten.Storage
{
    public class DocumentMetadata
    {
        public DocumentMetadata(DateTime lastModified, Guid currentVersion, string dotNetType, string documentType,
            bool deleted, DateTime? deletedAt)
        {
            LastModified = lastModified;
            CurrentVersion = currentVersion;
            DotNetType = dotNetType;
            DocumentType = documentType;
            Deleted = deleted;
            DeletedAt = deletedAt;
        }

        public DateTime LastModified { get; }
        public Guid CurrentVersion { get; }
        public string DotNetType { get; }
        public string DocumentType { get; }
        public bool Deleted { get; }
        public DateTime? DeletedAt { get; }
    }
}