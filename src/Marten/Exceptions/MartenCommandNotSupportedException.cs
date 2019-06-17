using System;
using System.Text.RegularExpressions;
using Marten.Services;
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
            Exception innerException,
            string message = null
        ) : base(command, innerException, message)
        {
            Reason = reason;
        }
    }

    public sealed class KnownExceptionCause
    {
        private readonly Func<Exception, bool> match;

        public KnownExceptionCause(string description, NotSupportedReason reason, Func<Exception, bool> match)
        {
            Description = description;
            Reason = reason;
            this.match = match;
        }

        public static readonly KnownExceptionCause ToTsvectorOnJsonb = new KnownExceptionCause("Full Text Search needs at least Postgres version 10.",
            NotSupportedReason.FullTextSearchNeedsAtLeastPostgresVersion10,
            e => e is PostgresException pe && pe.SqlState == PostgresErrorCodes.FunctionDoesNotExist &&
                 new Regex(@"function to_tsvector\((?:regconfig, )?jsonb\) does not exist").IsMatch(pe.Message));

        public static readonly KnownExceptionCause[] KnownCauses = { ToTsvectorOnJsonb };

        public NotSupportedReason Reason { get; }
        public string Description { get; }

        public bool Matches(Exception e)
        {
            return match(e);
        }
    }
}
