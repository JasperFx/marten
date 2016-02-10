using System;
using System.Data;
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

        public void Execute(NpgsqlCommand cmd, Action<NpgsqlCommand> action = null)
        {
            if (action == null)
            {
                action = c => c.ExecuteNonQuery();
            }

            execute(conn =>
            {
                cmd.Connection = conn;
                action(cmd);
            });
        }

        public void Execute(Action<NpgsqlCommand> action)
        {
            execute(conn =>
            {
                var cmd = conn.CreateCommand();
                action(cmd);
            });
        }

        public T Execute<T>(Func<NpgsqlCommand, T> func)
        {
            return execute(conn =>
            {
                var cmd = conn.CreateCommand();
                return func(cmd);
            });
        }

        public T Execute<T>(NpgsqlCommand cmd, Func<NpgsqlCommand, T> func)
        {
            return execute(conn =>
            {
                cmd.Connection = conn;
                return func(cmd);
            });
        }

        public Task ExecuteAsync(Func<NpgsqlCommand, CancellationToken, Task> action, CancellationToken token = new CancellationToken())
        {
            return executeAsync(async (conn, tkn) =>
            {
                var cmd = conn.CreateCommand();
                await action(cmd, tkn);
            }, token);
        }

        public Task ExecuteAsync(NpgsqlCommand cmd, Func<NpgsqlCommand, CancellationToken, Task> action, CancellationToken token = new CancellationToken())
        {
            return executeAsync(async (conn, tkn) =>
            {
                cmd.Connection = conn;
                await action(cmd, tkn);
            }, token);
        }

        public Task<T> ExecuteAsync<T>(Func<NpgsqlCommand, CancellationToken, Task<T>> func, CancellationToken token = new CancellationToken())
        {
            return executeAsync(async (conn, tkn) =>
            {
                var cmd = conn.CreateCommand();
                return await func(cmd, tkn);
            }, token);
        }

        public Task<T> ExecuteAsync<T>(NpgsqlCommand cmd, Func<NpgsqlCommand, CancellationToken, Task<T>> func, CancellationToken token = new CancellationToken())
        {
            return executeAsync(async (conn, tkn) =>
            {
                cmd.Connection = conn;
                return await func(cmd, tkn);
            }, token);
        }

        public void InTransaction(Action action)
        {
            action();
        }

        public void InTransaction(IsolationLevel level, Action action)
        {
            action();
        }

        
        public void execute(Action<NpgsqlConnection> action)
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

        public async Task executeAsync(Func<NpgsqlConnection, CancellationToken, Task> action, CancellationToken token)
        {
            using (var conn = _factory.Create())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                try
                {
                    await action(conn, token).ConfigureAwait(false);
                }
                finally
                {
                    conn.Close();
                }
            }
        }


        public T execute<T>(Func<NpgsqlConnection, T> func)
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

        public async Task<T> executeAsync<T>(Func<NpgsqlConnection, CancellationToken, Task<T>> func, CancellationToken token)
        {
            using (var conn = _factory.Create())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                try
                {
                    return await func(conn, token).ConfigureAwait(false);
                }
                finally
                {
                    conn.Close();
                }
            }
        }


        public void Dispose()
        {
            
        }
    }
}