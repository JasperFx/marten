using System;
using System.Collections.Generic;
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
            IncrementRequestCount();

            _commandRunner.Execute(action);
        }
        
        public void ExecuteInTransaction(Action<NpgsqlConnection> action)
        {
            IncrementRequestCount();

            _commandRunner.ExecuteInTransaction(action);
        }

        public T Execute<T>(Func<NpgsqlConnection, T> func)
        {
            IncrementRequestCount();

            return _commandRunner.Execute(func);
        }

        public IEnumerable<string> QueryJson(NpgsqlCommand cmd)
        {
            IncrementRequestCount();

            return _commandRunner.QueryJson(cmd);
        }

        public int Execute(string sql)
        {
            IncrementRequestCount();

            return _commandRunner.Execute(sql);
        }

        public T QueryScalar<T>(string sql)
        {
            IncrementRequestCount();

            return _commandRunner.QueryScalar<T>(sql);
        }

        private void IncrementRequestCount()
        {
            _threshold.ValidateCounter(++NumberOfRequests);
        }
    }
}