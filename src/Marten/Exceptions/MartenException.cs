using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions;

/// <summary>
///     Base class for all Marten related exceptions
/// </summary>
public class MartenException: Exception
{
    public MartenException()
    {
    }

    protected MartenException(SerializationInfo info, StreamingContext context): base(info, context)
    {
    }

    public MartenException(string message): base(message)
    {
    }

    public MartenException(string message, Exception innerException): base(message, innerException)
    {
    }
}
