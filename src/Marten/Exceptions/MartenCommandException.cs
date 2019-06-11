using System;
using Npgsql;

namespace Marten
{
    public class MartenCommandException: Exception
    {
        public NpgsqlCommand Command { get; }

        protected static string ToMessage(
            NpgsqlCommand command,
            string prefix = null
        )
        {
            if (prefix != null)
            {
                prefix = $"{prefix}\n";
            }

            return $"Marten Command Failure:\n{prefix}{command.CommandText}\n\n";
        }

        public MartenCommandException(
            NpgsqlCommand command,
            Exception innerException,
            string prefix = null
        ) : base(ToMessage(command, prefix) + innerException.Message, innerException)
        {
            Command = command;
        }
    }
}
