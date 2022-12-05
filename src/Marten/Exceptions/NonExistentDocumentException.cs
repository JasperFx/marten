using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions;

public class NonExistentDocumentException: MartenException
{
    public NonExistentDocumentException(Type docType, object id): base($"Nonexistent document {docType.FullName}: {id}")
    {
        DocType = docType;
        Id = id;
    }

    protected NonExistentDocumentException(SerializationInfo info, StreamingContext context): base(info, context)
    {
    }

    public Type DocType { get; }
    public object Id { get; }
}
