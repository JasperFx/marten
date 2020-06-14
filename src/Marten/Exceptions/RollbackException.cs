
using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions
{
    public class RollbackException: Exception
    {
        public RollbackException(Exception innerException) : base("Failed while trying to rollback an exception", innerException)
        {
        }

        protected RollbackException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
