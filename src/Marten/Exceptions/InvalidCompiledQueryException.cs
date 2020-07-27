using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions
{
    public class InvalidCompiledQueryException: Exception
    {
        public InvalidCompiledQueryException(string message) : base(message)
        {
        }

        public InvalidCompiledQueryException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected InvalidCompiledQueryException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
