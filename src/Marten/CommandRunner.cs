using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
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

        public CommandRunner(NpgsqlConnection connection)
        {
            _connection = connection;
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

        public IEnumerable<string> SchemaTableNames()
        {
            var table = _connection.GetSchema("Tables");
            var tables = new List<string>();
            foreach (DataRow row in table.Rows)
            {
                tables.Add(row[2].ToString());
            }

            return tables.Where(name => name.StartsWith("mt_")).ToArray();
        }

        public IEnumerable<string> SchemaFunctionNames()
        {
            return findFunctionNames().ToArray();
        }

        private IEnumerable<string> findFunctionNames()
        {
            ensureConnectionIsOpen();

            var sql = @"
SELECT routine_name
FROM information_schema.routines
WHERE specific_schema NOT IN ('pg_catalog', 'information_schema')
AND type_udt_name != 'trigger';
";

            var command = _connection.CreateCommand();
            command.CommandText = sql;

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    yield return reader.GetString(0);
                }
            }
        }

        public void DescribeSchema()
        {
            ensureConnectionIsOpen();

            var table = _connection.GetSchema();

            foreach (DataRow row in table.Rows)
            {
                Debug.WriteLine(row[0] + " / " + row[1] + " / " + row[2]);
            }

            Debug.WriteLine(table);
        }

        public T QueryScalar<T>(string sql)
        {
            ensureConnectionIsOpen();

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = sql;
                return (T) command.ExecuteScalar();
            }
        }
    }
}