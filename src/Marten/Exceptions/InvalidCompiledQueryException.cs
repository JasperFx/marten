using System;

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
    }
}
