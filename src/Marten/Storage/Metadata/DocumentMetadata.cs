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

        /// <summary>
        /// The identity of the document
        /// </summary>
        public object Id { get;}

        /// <summary>
        /// Timestamp of the last time this document was modified
        /// </summary>
        public DateTimeOffset LastModified { get; internal set;}

        /// <summary>
        /// The current version of this document in the database
        /// </summary>
        public Guid CurrentVersion { get; internal set;}

        /// <summary>
        /// The full name of the .Net type that was persisted
        /// </summary>
        public string DotNetType { get; internal set;}

        /// <summary>
        /// If the document is part of a type hierarchy, this designates
        /// Marten's internal name for the sub type
        /// </summary>
        public string DocumentType { get; internal set;}

        /// <summary>
        /// If soft-deleted, whether or not the document is marked as deleted
        /// </summary>
        public bool Deleted { get; internal set;}

        /// <summary>
        /// If soft-deleted, the time at which the document was marked as deleted
        /// </summary>
        public DateTimeOffset? DeletedAt { get; internal set; }

        /// <summary>
        /// The stored tenant id of this document
        /// </summary>
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
