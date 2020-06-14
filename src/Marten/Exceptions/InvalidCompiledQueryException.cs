using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions
{
    public class InvalidCompiledQueryException: Exception
    {
        public InvalidCompiledQueryException(string message) : base(message)
        {
        }

        protected InvalidCompiledQueryException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
