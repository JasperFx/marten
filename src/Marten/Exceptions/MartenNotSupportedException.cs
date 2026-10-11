using System;

namespace Marten.Exceptions;

public sealed class MartenNotSupportedException: MartenException
{
    public MartenNotSupportedException(string message): base(message)
    {
    }

    /// <summary>
    ///     Keeps the underlying failure — an unactionable serializer error, say — reachable while
    ///     Marten explains the remedy in <see cref="Exception.Message" />.
    /// </summary>
    public MartenNotSupportedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
