using System;
using Npgsql;

namespace Marten
{
    /// <summary>
    /// Wraps the Postgres command exceptions. Unifies exception handling and brings additonal information.
    /// </summary>
    public class MartenCommandException: Exception
    {
        /// <summary>
        /// Failed Postgres command
        /// </summary>
        public NpgsqlCommand Command { get; }

        protected static string ToMessage(
            NpgsqlCommand command,
            string prefix = null
        )
        {
            if (prefix != null)
            {
                prefix = $"{prefix}${Environment.NewLine}";
            }

            return $"Marten Command Failure:${Environment.NewLine}{prefix}{command.CommandText}${Environment.NewLine}${Environment.NewLine}";
        }

        /// <summary>
        /// Creates MartenCommandException based on the command and innerException information with formatted message.
        /// </summary>
        /// <param name="command">failed Postgres command</param>
        /// <param name="innerException">internal exception details</param>
        public MartenCommandException(NpgsqlCommand command, Exception innerException)
            : base(ToMessage(command) + innerException.Message, innerException)
        {
            Command = command;
        }

        /// <summary>
        /// Creates MartenCommandException based on the command and innerException information with formatted message.
        /// </summary>
        /// <param name="command">failed Postgres command</param>
        /// <param name="innerException">internal exception details</param>
        /// <param name="prefix">prefix that will be added to message</param>
        public MartenCommandException(
            NpgsqlCommand command,
            Exception innerException,
            string prefix
        ) : base(ToMessage(command, prefix) + innerException.Message, innerException)
        {
            Command = command;
        }
    }
}
