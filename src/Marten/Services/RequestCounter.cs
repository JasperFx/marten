using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Services
{
    public class RequestCounter : ICommandRunner
    {
        private readonly ICommandRunner _commandRunner;
        private readonly RequestCounterThreshold _threshold;
        public int NumberOfRequests { get; private set; }

        public RequestCounter(ICommandRunner commandRunner) : this(commandRunner, RequestCounterThreshold.Empty)
        {
            
        }

        public RequestCounter(ICommandRunner commandRunner, RequestCounterThreshold threshold)
        {
            _commandRunner = commandRunner;
            _threshold = threshold;
        }

        public void Execute(Action<NpgsqlConnection> action)
        {
            incrementRequestCount();
            _commandRunner.Execute(action);
        }

        public Task ExecuteAsync(Func<NpgsqlConnection, CancellationToken, Task> action, CancellationToken token = new CancellationToken())
        {
            incrementRequestCount();
            return _commandRunner.ExecuteAsync(action, token);
        }

        public T Execute<T>(Func<NpgsqlConnection, T> func)
        {
            incrementRequestCount();
            return _commandRunner.Execute(func);
        }

        public Task<T> ExecuteAsync<T>(Func<NpgsqlConnection, CancellationToken, Task<T>> func, CancellationToken token = new CancellationToken())
        {
            incrementRequestCount();
            return _commandRunner.ExecuteAsync(func, token);
        }

        private void incrementRequestCount()
        {
            _threshold.ValidateCounter(++NumberOfRequests);
        }
    }
}