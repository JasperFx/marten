using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
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


        public int Execute(string sql)
        {
            using (var conn = _factory.Create())
            {
                conn.Open();

                try
                {
                    using (var command = conn.CreateCommand())
                    {
                        command.CommandText = sql;
                        return command.ExecuteNonQuery();
                    }
                }
                finally
                {
                    conn.Close();
                }
            }
        }


        public int Execute(string function, Action<NpgsqlCommand> configure)
        {
            using (var conn = _factory.Create())
            {
                conn.Open();

                try
                {
                    using (var command = conn.CreateCommand())
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.CommandText = function;

                        configure(command);



                        return command.ExecuteNonQuery();
                    }
                }
                finally
                {
                    conn.Close();
                }
            }


        }


        public IEnumerable<string> SchemaTableNames()
        {
            using (var conn = _factory.Create())
            {
                try
                {
                    var table = conn.GetSchema("Tables");
                    var tables = new List<string>();
                    foreach (DataRow row in table.Rows)
                    {
                        tables.Add(row[2].ToString());
                    }

                    return tables.Where(name => name.StartsWith("mt_")).ToArray();
                }
                finally
                {
                    conn.Close();
                }
            }


        }

        public IEnumerable<string> SchemaFunctionNames()
        {
            return findFunctionNames().ToArray();
        }

        private IEnumerable<string> findFunctionNames()
        {
            using (var conn = _factory.Create())
            {
                conn.Open();

                try
                {
                    var sql = @"
SELECT routine_name
FROM information_schema.routines
WHERE specific_schema NOT IN ('pg_catalog', 'information_schema')
AND type_udt_name != 'trigger';
";

                    var command = conn.CreateCommand();
                    command.CommandText = sql;

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            yield return reader.GetString(0);
                        }

                        reader.Close();
                    }
                }
                finally
                {
                    conn.Close();
                }
            }


        }


        public T QueryScalar<T>(string sql)
        {
            using (var conn = _factory.Create())
            {
                conn.Open();

                try
                {
                    using (var command = conn.CreateCommand())
                    {
                        command.CommandText = sql;
                        return (T)command.ExecuteScalar();
                    }
                }
                finally
                {
                    conn.Close();
                }
            }


        }
    }
}