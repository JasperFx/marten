using System;

namespace Marten.Exceptions
{
    public class InvalidStreamOperationException: Exception
    {
        public InvalidStreamOperationException(string message):
            base(message)
        {

        }
    }
}
