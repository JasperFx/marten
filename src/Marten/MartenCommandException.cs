using System;
using Npgsql;

namespace Marten
{
    public class MartenCommandException : Exception
    {
        public NpgsqlCommand Command { get; }

        private static string toMessage(NpgsqlCommand command)
        {
            return $"Marten Command Failure:\n{command.CommandText}\n\n";
        }

        public MartenCommandException(NpgsqlCommand command, Exception innerException) : base(toMessage(command) + innerException.Message, innerException)
        {
            Command = command;
        }
    }
}