using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions
{
    public class ConcurrentUpdateException: MartenException
    {
        public ConcurrentUpdateException(Exception innerException) : base("Write collision detected while commiting the transaction.", innerException)
        {
        }

        protected ConcurrentUpdateException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
