using System;
using Baseline.Exceptions;
#nullable enable
namespace Marten.Exceptions
{
    public class InvalidUtcDateTimeUsageException : Exception
    {
        public InvalidUtcDateTimeUsageException(Exception inner) : base("DateTime with Kind=UTC is no longer supported by Npgsql. Consider switching to DateTimeOffset or NodaTime wherever possible, or see https://www.npgsql.org/efcore/release-notes/6.0.html.", inner)
        {
        }
    }

    internal class UtcDateTimeUsageExceptionTransform: IExceptionTransform
    {
        public bool TryTransform(Exception original, out Exception? transformed)
        {
            if (original is InvalidCastException &&
                original.Message.Contains("Cannot write DateTime with Kind=UTC to PostgreSQL"))
            {
                transformed = new InvalidUtcDateTimeUsageException(original);
                return true;
            }

            transformed = null;
            return false;
        }
    }
}
