using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Npgsql;

namespace Marten
{
    public class CommandRunner
    {
        private readonly IConnectionFactory _factory;

        public CommandRunner(IConnectionFactory factory)
        {
            _factory = factory;
        }

        public void Execute(Action<NpgsqlConnection> action)
        {
            using (var conn = _factory.Create())
            {
                conn.Open();

                try
                {
                    action(conn);
                }
                finally
                {
                    conn.Close();
                }
            }
        }

        public T Execute<T>(Func<NpgsqlConnection, T> func)
        {
            using (var conn = _factory.Create())
            {
                conn.Open();

                try
                {
                    return func(conn);
                }
                finally
                {
                    conn.Close();
                }
            }
        }

        public int Execute(string sql)
        {
            return Execute(conn =>
            {
                using (var command = conn.CreateCommand())
                {
                    command.CommandText = sql;
                    return command.ExecuteNonQuery();
                }
            });
        }



        public IEnumerable<string> SchemaTableNames()
        {
            return Execute(conn =>
            {
                var table = conn.GetSchema("Tables");
                var tables = new List<string>();
                foreach (DataRow row in table.Rows)
                {
                    tables.Add(row[2].ToString());
                }

                return tables.Where(name => name.StartsWith("mt_")).ToArray();
            });
        }

        public IEnumerable<string> SchemaFunctionNames()
        {
            return findFunctionNames().ToArray();
        }

        private IEnumerable<string> findFunctionNames()
        {
            return Execute(conn =>
            {
                    var sql = @"
SELECT routine_name
FROM information_schema.routines
WHERE specific_schema NOT IN ('pg_catalog', 'information_schema')
AND type_udt_name != 'trigger';
";

                    var command = conn.CreateCommand();
                    command.CommandText = sql;

                var list = new List<string>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(reader.GetString(0));
                    }

                    reader.Close();
                }

                return list;
            });

        }


        public T QueryScalar<T>(string sql)
        {
            return Execute(conn =>
            {
                using (var command = conn.CreateCommand())
                {
                    command.CommandText = sql;
                    return (T)command.ExecuteScalar();
                }
            });
        }
    }
}