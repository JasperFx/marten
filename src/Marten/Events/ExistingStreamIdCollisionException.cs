using System;

namespace Marten.Events
{
    public class ExistingStreamIdCollisionException : Exception
    {
        public object Id { get; }

        public Type AggregateType { get; }

        public ExistingStreamIdCollisionException(object id, Type aggregateType) : base($"Stream #{id} already exists in the database")
        {
            Id = id;
            AggregateType = aggregateType;
        }
    }
}