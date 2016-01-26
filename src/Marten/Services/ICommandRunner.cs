using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Util;
using Npgsql;

namespace Marten.Services
{
    public interface ICommandRunner
    {
        void Execute(Action<NpgsqlConnection> action);
        Task ExecuteAsync(Func<NpgsqlConnection, CancellationToken, Task> action, CancellationToken token = default (CancellationToken));
        void ExecuteInTransaction(Action<NpgsqlConnection> action);
        Task ExecuteInTransactionAsync(Func<NpgsqlConnection, CancellationToken, Task> action, CancellationToken token = default(CancellationToken));

        T Execute<T>(Func<NpgsqlConnection, T> func);
        Task<T> ExecuteAsync<T>(Func<NpgsqlConnection, CancellationToken, Task<T>> func, CancellationToken token = default(CancellationToken));
        IEnumerable<string> QueryJson(NpgsqlCommand cmd);
        Task<IEnumerable<string>> QueryJsonAsync(NpgsqlCommand cmd, CancellationToken token = default(CancellationToken));
        int Execute(string sql);
        Task<int> ExecuteAsync(string sql, CancellationToken token = default(CancellationToken));
        T QueryScalar<T>(string sql);
        Task<T> QueryScalarAsync<T>(string sql, CancellationToken token = default(CancellationToken));
    }

    public static class CommandRunnerExtensions
    {
        public static IList<string> GetStringList(this ICommandRunner runner, string sql, params object[] parameters)
        {
            var list = new List<string>();

            runner.Execute(conn =>
            {
                var cmd = conn.CreateCommand(sql);
                parameters.Each(x =>
                {
                    var param = cmd.AddParameter(x);
                    cmd.CommandText = cmd.CommandText.UseParameter(param);
                });

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

        public static IEnumerable<T> Fetch<T>(this ICommandRunner runner, string sql, Func<IDataReader, T> transform, params object[] parameters)
        {
            return runner.Execute(conn =>
            {
                try
                {
                    return conn.Fetch(sql, transform, parameters);
                }
                catch (Exception e)
                {
                    throw new Exception($"Error trying to fetch w/ sql '{sql}'", e);
                }
            });
        } 
    }
}