using System;

namespace Marten.Services
{
    public class Delete
    {
        public Type DocumentType { get; }
        public object Id { get; }
        public object Document { get; }

        public Delete(Type documentType, object id, object document = null)
        {
            DocumentType = documentType;
            Id = id;
            Document = document;
        }
    }
}