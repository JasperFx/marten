using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        public async Task ExecuteAsync(Func<NpgsqlConnection, CancellationToken, Task> action, CancellationToken token)
        {
            using (var conn = _factory.Create())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                try
                {
                    await action(conn, token);
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

        public async Task ExecuteInTransactionAsync(Func<NpgsqlConnection, CancellationToken, Task> action, CancellationToken token)
        {
            await ExecuteAsync(async (conn, tkn) =>
            {
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        await action(conn, tkn);

                        tx.Commit();
                    }
                    catch (Exception)
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }, token).ConfigureAwait(false);
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

        public async Task<T> ExecuteAsync<T>(Func<NpgsqlConnection, CancellationToken, Task<T>> func, CancellationToken token)
        {
            using (var conn = _factory.Create())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                try
                {
                    return await func(conn, token);
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

        public async Task<IEnumerable<string>> QueryJsonAsync(NpgsqlCommand cmd, CancellationToken token)
        {
            return await ExecuteAsync(async (conn, tkn) =>
            {
                cmd.Connection = conn;

                var list = new List<string>();
                using (var reader = await cmd.ExecuteReaderAsync(tkn))
                {
                    while (await reader.ReadAsync(tkn))
                    {
                        list.Add(reader.GetString(0));
                    }

                    reader.Close();
                }

                return list;
            }, token).ConfigureAwait(false);
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

        public async Task<int> ExecuteAsync(string sql, CancellationToken token)
        {
            return await ExecuteAsync(async (conn, tkn) =>
            {
                using (var command = conn.CreateCommand())
                {
                    command.CommandText = sql;
                    return await command.ExecuteNonQueryAsync(tkn);
                }
            }, token).ConfigureAwait(false);
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

        public async Task<T> QueryScalarAsync<T>(string sql, CancellationToken token)
        {
            return await ExecuteAsync(async (conn, tkn) =>
            {
                using (var command = conn.CreateCommand())
                {
                    command.CommandText = sql;
                    var result = await command.ExecuteScalarAsync(tkn);
                    return (T)result;
                }
            }, token).ConfigureAwait(false);
        }
    }
}