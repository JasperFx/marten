namespace Marten.Exceptions;

public sealed class MartenNotSupportedException(string message) : MartenException(message);
