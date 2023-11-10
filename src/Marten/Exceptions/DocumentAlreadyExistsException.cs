using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions;

public sealed class DocumentAlreadyExistsException: MartenException
{
    public DocumentAlreadyExistsException(Exception inner, Type docType, object id, string constraintName): base(
        $"Document already exists {docType.FullName}: {id}", inner)
    {
        DocType = docType;
        Id = id;
        ConstraintName = constraintName;
    }

    public DocumentAlreadyExistsException(SerializationInfo info, StreamingContext context): base(info, context)
    {
    }

    public Type DocType { get; }
    public object Id { get; }
    public string ConstraintName { get; }
}
