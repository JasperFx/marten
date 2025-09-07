using System;
using System.Diagnostics.CodeAnalysis;
using JasperFx.Core.Exceptions;

namespace Marten.Exceptions;

internal class DateTimeUsageExceptionTransform: IExceptionTransform
{
    public bool TryTransform(Exception original, [NotNullWhen(true)]out Exception? transformed)
    {
        if (original is InvalidCastException ice &&
            ice.Message.Contains(
                "Reading as 'System.IO.TextReader' is not supported for fields having DataTypeName 'timestamp without time zone"))
        {
            transformed = new InvalidDateTimeUsageException(
                "DateTime types are no longer usable with Npgsql for this use case. If using a DateTime member for optimized querying, consider using a computed index instead. Or switch this member to DateTimeOffset or use NodaTime. See https://www.npgsql.org/efcore/release-notes/6.0.html",
                original);
            return true;
        }

        transformed = null;
        return false;
    }
}

public class InvalidDateTimeUsageException: MartenException
{
    public InvalidDateTimeUsageException(string message, Exception innerException): base(message, innerException)
    {
    }
}

public class InvalidUtcDateTimeUsageException: MartenException
{
    public InvalidUtcDateTimeUsageException(Exception inner): base(
        "DateTime with Kind=UTC is no longer supported by Npgsql. Consider switching to DateTimeOffset or NodaTime wherever possible, or see https://www.npgsql.org/efcore/release-notes/6.0.html.",
        inner)
    {
    }
}

internal class UtcDateTimeUsageExceptionTransform: IExceptionTransform
{
    public bool TryTransform(Exception original, [NotNullWhen(true)]out Exception? transformed)
    {
        if ((original is InvalidCastException or ArgumentException) &&
            original.Message.Contains("Cannot write DateTime with Kind=UTC to PostgreSQL"))
        {
            transformed = new InvalidUtcDateTimeUsageException(original);
            return true;
        }

        transformed = null;
        return false;
    }
}
