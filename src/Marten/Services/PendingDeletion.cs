using System;

namespace Marten.Services
{
    public class PendingDeletion
    {
        public Type DocumentType { get; }
        public object Id { get; }
        public object Document { get; }

        public PendingDeletion(Type documentType, object id, object document = null)
        {
            DocumentType = documentType;
            Id = id;
            Document = document;
        }
    }
}