using System;
using System.Runtime.Serialization;

namespace Marten.Linq
{
    [Serializable]
    public class BadLinqExpressionException : Exception
    {
        public BadLinqExpressionException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected BadLinqExpressionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}