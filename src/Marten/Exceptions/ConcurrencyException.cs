using System;
using System.Runtime.Serialization;
using JasperFx.Core.Reflection;
using Marten.Metadata;

namespace Marten.Exceptions;

public class ConcurrencyException: MartenException
{
    public static string ToMessage(Type docType, object id)
    {
        var message = $"Optimistic concurrency check failed for {docType.FullName} #{id}";
        if (docType.CanBeCastTo<IRevisioned>())
        {
            message +=
                $". For documents of type {typeof(IRevisioned).FullNameInCode()}, Marten uses the current value of {nameof(IRevisioned)}.{nameof(IRevisioned.Version)} as the revision when IDocumentSession.Store() is called. You may need to explicitly call IDocumentSession.UpdateRevision() instead, or set the expected version correctly on the document itself";
        }

        return message;
    }

    public ConcurrencyException(Type docType, object id): base(ToMessage(docType, id))
    {
        DocType = docType.FullName;
        Id = id;
    }

    public ConcurrencyException(string message, Type docType, object id): base(message)
    {
        DocType = docType?.FullName;
        Id = id;
    }


    protected ConcurrencyException(SerializationInfo info, StreamingContext context): base(info, context)
    {
    }

    public string DocType { get; set; }
    public object Id { get; set; }
}
