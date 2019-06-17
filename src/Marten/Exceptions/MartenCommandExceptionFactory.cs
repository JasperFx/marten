using System;
using System.Linq;
using Marten.Services;
using Npgsql;

namespace Marten.Exceptions
{
    /// <summary>
    /// Class responsible for MartenCommandException creation.
    /// Based on the exact command code and exception creates
    /// generi MartenCommandException or specific Exception derived from it.
    /// </summary>
    internal static class MartenCommandExceptionFactory
    {
        internal static MartenCommandException Create
        (
            NpgsqlCommand command,
            Exception innerException
        )
        {
            if (TryToMapToMartenCommandNotSupportedException(command, innerException, out var notSupportedException))
            {
                return notSupportedException;
            }

            return new MartenCommandException(command, innerException);
        }

        internal static bool TryToMapToMartenCommandNotSupportedException
        (
            NpgsqlCommand command,
            Exception innerException,
            out MartenCommandNotSupportedException notSupportedException
        )
        {
            var knownCause = KnownExceptionCause.KnownCauses.FirstOrDefault(x => x.Matches(innerException));

            if (knownCause != null)
            {
                notSupportedException = new MartenCommandNotSupportedException(knownCause.Reason, command, innerException, knownCause.Description);

                return true;
            }

            notSupportedException = null;
            return false;
        }
    }


}
