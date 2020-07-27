using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions
{
    public class NonExistentDocumentException: Exception
    {
        public Type DocType { get; }
        public object Id { get; }

        public NonExistentDocumentException(Type docType, object id) : base((string)$"Nonexistent document {docType.FullName}: {id}")
        {
            DocType = docType;
            Id = id;
        }

        protected NonExistentDocumentException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
