using System;
using System.Collections.Generic;

namespace Marten.Storage.Metadata
{
    public class DocumentMetadata
    {
        public DocumentMetadata(object id)
        {
            Id = id;
        }

        public object Id { get;}

        public DateTimeOffset LastModified { get; internal set;}
        public Guid CurrentVersion { get; internal set;}
        public string DotNetType { get; internal set;}
        public string DocumentType { get; internal set;}
        public bool Deleted { get; internal set;}
        public DateTimeOffset? DeletedAt { get; internal set; }
        public string TenantId { get; internal set; }

        /// <summary>
        /// Optional metadata describing the causation id for this
        /// unit of work
        /// </summary>
        public string CausationId { get; set; }

        /// <summary>
        /// Optional metadata describing the correlation id for this
        /// unit of work
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// Optional metadata describing the user name or
        /// process name for this unit of work
        /// </summary>
        public string LastModifiedBy { get; set; }

        /// <summary>
        /// Optional, user defined headers
        /// </summary>
        public Dictionary<string, object> Headers { get; set; } = new Dictionary<string, object>();
    }
}
