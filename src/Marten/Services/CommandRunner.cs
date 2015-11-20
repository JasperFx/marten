using System;
using System.Collections.Generic;
using Npgsql;

namespace Marten.Services
{
    public class CommandRunner : ICommandRunner
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

        public void ExecuteInTransaction(Action<NpgsqlConnection> action)
        {
            Execute(conn =>
            {
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        action(conn);

                        tx.Commit();
                    }
                    catch (Exception)
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            });

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
    }
}