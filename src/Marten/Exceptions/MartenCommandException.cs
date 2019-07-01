using System;
using Npgsql;

namespace Marten.Exceptions
{
    /// <summary>
    /// Wraps the Postgres command exceptions. Unifies exception handling and brings additonal information.
    /// </summary>
    public class MartenCommandException: Marten.MartenCommandException
    {
        public MartenCommandException(NpgsqlCommand command, Exception innerException) : base(command, innerException)
        {
        }

        public MartenCommandException(NpgsqlCommand command, Exception innerException, string prefix) : base(command, innerException, prefix)
        {
        }
    }
}
