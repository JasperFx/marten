using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions;

public class ExistingStreamIdCollisionException: MartenException
{
    public ExistingStreamIdCollisionException(object id, Type aggregateType): base(
        $"Stream #{id} already exists in the database. StartStream requires a new id; to add events to an existing stream use Append (which starts the stream if missing) or FetchForWriting, and make create commands idempotent on the stream id.")
    {
        Id = id;
        AggregateType = aggregateType;
    }

    protected ExistingStreamIdCollisionException(SerializationInfo info, StreamingContext context): base(info, context)
    {
    }

    public object Id { get; }

    public Type AggregateType { get; }
}
