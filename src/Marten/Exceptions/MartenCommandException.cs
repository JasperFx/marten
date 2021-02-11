using System;
using Npgsql;

namespace Marten.Exceptions
{
    /// <summary>
    /// Wraps the Postgres command exceptions. Unifies exception handling and brings additonal information.
    /// </summary>
    public class MartenCommandException: Exception
    {
        public const string MaybeLockedRowsMessage = "Postgresql timed out while trying to read data. This may be caused by trying to read locked rows";

        /// <summary>
        /// Failed Postgres command
        /// </summary>
        public NpgsqlCommand Command { get; }

        protected static string ToMessage(NpgsqlCommand command,
            Exception innerException,
            string prefix = null)
        {
            if (prefix != null)
            {
                prefix = $"{prefix}${Environment.NewLine}";
            }

            var explanation = "";
            if (innerException is NpgsqlException pgex)
            {
                if (pgex.InnerException is TimeoutException toex)
                {
                    if (toex.Message == "Timeout during reading attempt")
                    {
                        explanation = Environment.NewLine +
                                      MaybeLockedRowsMessage + Environment.NewLine;
                    }
                }
            }


            return $"Marten Command Failure:${Environment.NewLine}{prefix}{explanation}{command.CommandText}${Environment.NewLine}${Environment.NewLine}";
        }

        /// <summary>
        /// Creates MartenCommandException based on the command and innerException information with formatted message.
        /// </summary>
        /// <param name="command">failed Postgres command</param>
        /// <param name="innerException">internal exception details</param>
        public MartenCommandException(NpgsqlCommand command, Exception innerException)
            : base(ToMessage(command, innerException) + innerException.Message, innerException)
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
        ) : base(ToMessage(command, innerException, prefix) + innerException.Message, innerException)
        {
            Command = command;
        }
    }
}
