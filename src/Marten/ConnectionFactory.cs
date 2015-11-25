using System;
using Npgsql;

namespace Marten
{
    public class ConnectionFactory : IConnectionFactory
    {
        private readonly Lazy<string> _connectionString;

        public ConnectionFactory(Func<string> connectionSource)
        {
            _connectionString = new Lazy<string>(connectionSource);
        }

        public ConnectionFactory(string connectionString)
        {
            _connectionString = new Lazy<string>(() => connectionString);
        }

        public NpgsqlConnection Create()
        {
            return new NpgsqlConnection(_connectionString.Value);
        }
    }
}