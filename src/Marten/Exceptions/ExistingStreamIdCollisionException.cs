using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions
{
    public class ExistingStreamIdCollisionException: Exception
    {
        public object Id { get; }

        public Type AggregateType { get; }

        public ExistingStreamIdCollisionException(object id, Type aggregateType) : base($"Stream #{id} already exists in the database")
        {
            Id = id;
            AggregateType = aggregateType;
        }

        protected ExistingStreamIdCollisionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
