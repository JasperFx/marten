using System;
using System.Linq;
using Npgsql;

namespace Marten.Exceptions
{
    /// <summary>
    /// Class responsible for creating MartenCommandException exception or exceptions derived from it based on exact command code.
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
            var knownCause = KnownNotSupportedExceptionCause.KnownCauses.FirstOrDefault(x => x.Matches(innerException));

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
