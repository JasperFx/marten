using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions
{
    public sealed class DocumentAlreadyExistsException: Exception
    {
        public Type DocType { get; }
        public object Id { get; }

        public DocumentAlreadyExistsException(Exception inner, Type docType, object id) : base($"Document already exists {docType.FullName}: {id}", inner)
        {
            DocType = docType;
            Id = id;
        }

        private DocumentAlreadyExistsException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
