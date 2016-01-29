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
    public static class CommandRunnerExtensions
    {
        public static int Execute(this ICommandRunner runner, string sql)
        {
            return runner.Execute(conn =>
            {
                using (var command = conn.CreateCommand())
                {
                    command.CommandText = sql;
                    return command.ExecuteNonQuery();
                }
            });
        }

        public static Task<int> ExecuteAsync(this ICommandRunner runner, string sql, CancellationToken token)
        {
            return runner.ExecuteAsync(async (conn, tkn) =>
            {
                using (var command = conn.CreateCommand())
                {
                    command.CommandText = sql;
                    return await command.ExecuteNonQueryAsync(tkn).ConfigureAwait(false);
                }
            }, token);
        }


        public static IEnumerable<string> QueryJson(this ICommandRunner runner, NpgsqlCommand cmd)
        {
            return runner.Execute(conn =>
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

        public static async Task<IEnumerable<string>> QueryJsonAsync(this ICommandRunner runner, NpgsqlCommand cmd, CancellationToken token)
        {
            return await runner.ExecuteAsync(async (conn, tkn) =>
            {
                cmd.Connection = conn;

                var list = new List<string>();
                using (var reader = await cmd.ExecuteReaderAsync(tkn).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(tkn).ConfigureAwait(false))
                    {
                        list.Add(reader.GetString(0));
                    }

                    reader.Close();
                }

                return list;
            }, token).ConfigureAwait(false);
        }

        public static void ExecuteInTransaction(this ICommandRunner runner, Action<NpgsqlConnection, NpgsqlTransaction> action)
        {
            runner.Execute(conn =>
            {
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        action(conn, tx);

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

        public static Task ExecuteInTransactionAsync(this ICommandRunner runner, Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> action, CancellationToken token)
        {
            return runner.ExecuteAsync(async (conn, tkn) =>
            {
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        await action(conn, tx, tkn).ConfigureAwait(false);

                        tx.Commit();
                    }
                    catch (Exception)
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }, token);
        }

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

        public static T QueryScalar<T>(this ICommandRunner runner, string sql)
        {
            return runner.Execute(conn =>
            {
                using (var command = conn.CreateCommand())
                {
                    command.CommandText = sql;
                    return (T)command.ExecuteScalar();
                }
            });
        }

        public static Task<T> QueryScalarAsync<T>(this ICommandRunner runner, string sql, CancellationToken token)
        {
            return runner.ExecuteAsync(async (conn, tkn) =>
            {
                using (var command = conn.CreateCommand())
                {
                    command.CommandText = sql;
                    var result = await command.ExecuteScalarAsync(tkn).ConfigureAwait(false);
                    return (T)result;
                }
            }, token);
        }
    }
}