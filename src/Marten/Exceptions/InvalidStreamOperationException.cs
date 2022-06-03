using System;

namespace Marten.Exceptions
{
    public class InvalidStreamOperationException: MartenException
    {
        public InvalidStreamOperationException(string message):
            base(message)
        {

        }
    }
}
