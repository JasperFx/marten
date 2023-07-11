using System;
using System.Linq.Expressions;

namespace Marten.Exceptions;
#if SERIALIZE
    [Serializable]
#endif

public class BadLinqExpressionException: MartenException
{
    public BadLinqExpressionException(string message, Exception innerException): base(message, innerException)
    {
    }

    public BadLinqExpressionException(string message): base(message)
    {
    }

    public BadLinqExpressionException(Expression expression) : this($"Marten can not (yet) support the Linq expression '{expression}'")
    {
    }

#if SERIALIZE
        protected BadLinqExpressionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
}
