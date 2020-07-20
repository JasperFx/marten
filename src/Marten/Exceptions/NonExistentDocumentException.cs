using System;

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
    }
}
