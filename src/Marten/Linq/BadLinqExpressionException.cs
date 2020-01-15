using System;

namespace Marten.Linq
{
#if SERIALIZE
    [Serializable]
#endif

    public class BadLinqExpressionException: Exception
    {
        public BadLinqExpressionException(string message, Exception innerException) : base(message, innerException)
        {
        }

#if SERIALIZE
        protected BadLinqExpressionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }
}
