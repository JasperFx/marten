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
            ensureConnectionIsOpen();

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = sql;
                return command.ExecuteNonQuery();
            }
        }

        public int Execute(string function, Action<NpgsqlCommand> configure)
        {
            ensureConnectionIsOpen();

            using (var command = _connection.CreateCommand())
            {
                command.CommandType = CommandType.StoredProcedure;
                command.CommandText = function;

                configure(command);

                

                return command.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
            ensureConnectionIsOpen();

            _connection.Dispose();
        }

        private void ensureConnectionIsOpen()
        {
            if (_connection.State == ConnectionState.Closed)
            {
                _connection.Open();
            }
        }
    }
}