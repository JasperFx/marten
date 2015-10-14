using System;
using System.Data;
using Npgsql;

namespace Marten
{
    public class CommandRunner : IDisposable
    {
        private readonly NpgsqlConnection _connection;

        public CommandRunner(string connectionString)
        {
            _connection = new NpgsqlConnection(connectionString);
        }

        public int Execute(string sql)
        {
            if (_connection.State == ConnectionState.Closed)
            {
                _connection.Open();
            }

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = sql;
                return command.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
            if (_connection.State != ConnectionState.Closed)
            {
                _connection.Close();
            }

            _connection.Dispose();
        }
    }
}