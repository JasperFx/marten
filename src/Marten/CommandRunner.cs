using System;
using System.Collections.Generic;
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

        public IEnumerable<string> QueryJson(NpgsqlCommand cmd)
        {
            return Execute(conn =>
            {
                cmd.Connection = conn;

                var list = new List<string>();
                using (var reader = cmd.ExecuteReader())
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

        public IEnumerable<T> Query<T>(NpgsqlCommand cmd, ISerializer serializer)
        {
            return QueryJson(cmd).Select(serializer.FromJson<T>);
        }
    }
}