using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Services
{
    public interface ICommandRunner
    {
        void Execute(NpgsqlCommand cmd, Action<NpgsqlCommand> action = null);
        void Execute(Action<NpgsqlCommand> action);

        T Execute<T>(Func<NpgsqlCommand, T> func);
        T Execute<T>(NpgsqlCommand cmd, Func<NpgsqlCommand, T> func);


        Task ExecuteAsync(Func<NpgsqlCommand, CancellationToken, Task> action, CancellationToken token = default(CancellationToken));
        Task ExecuteAsync(NpgsqlCommand cmd, Func<NpgsqlCommand, CancellationToken, Task> action, CancellationToken token = default(CancellationToken));


        Task<T> ExecuteAsync<T>(Func<NpgsqlCommand, CancellationToken, Task<T>> func, CancellationToken token = default(CancellationToken));
        Task<T> ExecuteAsync<T>(NpgsqlCommand cmd, Func<NpgsqlCommand, CancellationToken, Task<T>> func, CancellationToken token = default(CancellationToken));

        void InTransaction(Action action);
        void InTransaction(IsolationLevel level, Action action);

    }
}