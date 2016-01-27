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





    }
}