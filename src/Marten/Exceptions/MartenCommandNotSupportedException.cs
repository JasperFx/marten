using System;
using Npgsql;

namespace Marten.Exceptions
{
    public enum NotSupportedReason
    {
        FullTextSearchNeedsAtLeastPostgresVersion10
    }

    public class MartenCommandNotSupportedException: MartenCommandException
    {
        public NotSupportedReason Reason { get; }

        public MartenCommandNotSupportedException(
            NotSupportedReason reason,
            NpgsqlCommand command,
            Exception innerException
        ) : base(command, innerException, MapReasonToMessage(reason))
        {
            Reason = reason;
        }

        private static string MapReasonToMessage(NotSupportedReason reason)
        {
            switch (reason)
            {
                case NotSupportedReason.FullTextSearchNeedsAtLeastPostgresVersion10:
                    return "Full Text Search needs at least Postgres version 10.";

                default:
                    return reason.ToString();
            }
        }
    }
}
