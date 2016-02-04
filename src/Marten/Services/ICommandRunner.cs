using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Services
{
    public interface ICommandRunner
    {
        void Execute(Action<NpgsqlConnection> action);
        Task ExecuteAsync(Func<NpgsqlConnection, CancellationToken, Task> action, CancellationToken token = default (CancellationToken));

        T Execute<T>(Func<NpgsqlConnection, T> func);
        Task<T> ExecuteAsync<T>(Func<NpgsqlConnection, CancellationToken, Task<T>> func, CancellationToken token = default(CancellationToken));
    }
}