using System;
using System.Collections.Generic;
using Marten.Map;
using Npgsql;

namespace Marten
{
    public interface ICommandRunner
    {
        void Execute(Action<NpgsqlConnection> action);
        T Execute<T>(Func<NpgsqlConnection, T> func);
        IEnumerable<string> QueryJson(NpgsqlCommand cmd);
        int Execute(string sql);
        T QueryScalar<T>(string sql);
    }

    public static class CommandRunnerExtensions
    {
        public static IList<string> GetStringList(this ICommandRunner runner, string sql)
        {
            var list = new List<string>();

            runner.Execute(conn =>
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(reader.GetString(0));
                    }

                    reader.Close();
                }
            });

            return list;
        }  
    }
}