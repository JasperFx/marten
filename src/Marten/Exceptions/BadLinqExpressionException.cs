using System;

namespace Marten.Exceptions
{
#if SERIALIZE
    [Serializable]
#endif

    public class BadLinqExpressionException: Exception
    {
        public BadLinqExpressionException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public BadLinqExpressionException(string message) : base(message)
        {
        }

#if SERIALIZE
        protected BadLinqExpressionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }
}
