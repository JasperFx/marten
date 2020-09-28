using System;

namespace Marten.Storage
{
    public class DocumentMetadata
    {
        public DocumentMetadata(object id)
        {
            Id = id;
        }

        public object Id { get;}

        public DateTime LastModified { get; internal set;}
        public Guid CurrentVersion { get; internal set;}
        public string DotNetType { get; internal set;}
        public string DocumentType { get; internal set;}
        public bool Deleted { get; internal set;}
        public DateTime? DeletedAt { get; internal set; }
        public string TenantId { get; internal set; }
    }
}
