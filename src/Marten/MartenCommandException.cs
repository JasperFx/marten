using System;
using Npgsql;

namespace Marten
{
    [Obsolete("This class is Obsolete, please use Marten.Exceptions.MartenCommandException.")]
    public class MartenCommandException: Exceptions.MartenCommandException
    {
        public MartenCommandException(NpgsqlCommand command, Exception innerException) : base(command, innerException)
        {
        }

        public MartenCommandException(NpgsqlCommand command, Exception innerException, string prefix) : base(command, innerException, prefix)
        {
        }
    }
}
