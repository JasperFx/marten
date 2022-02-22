using System;
using System.Diagnostics;
using System.Linq;
using Marten.Services;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit.Abstractions;

namespace Marten.Testing.Harness
{
    public class TestOutputMartenLogger : IMartenLogger, IMartenSessionLogger, ILogger
    {
        private ITestOutputHelper _output;
        private static ITestOutputHelper _noopTestOutputHelper = new NoopTestOutputHelper();

        public TestOutputMartenLogger(ITestOutputHelper output)
        {
            _output = output ?? _noopTestOutputHelper;
        }

        public IMartenSessionLogger StartSession(IQuerySession session)
        {
            return this;
        }

        public void SchemaChange(string sql)
        {
            _output.WriteLine("Executing DDL change:");
            _output.WriteLine(sql);
            _output.WriteLine(String.Empty);

            Debug.WriteLine("Executing DDL change:");
            Debug.WriteLine(sql);
            Debug.WriteLine(String.Empty);
        }

        public void LogSuccess(NpgsqlCommand command)
        {
            _output.WriteLine(command.CommandText);
            foreach (var p in command.Parameters.OfType<NpgsqlParameter>())
            {
                _output.WriteLine($"  {p.ParameterName}: {p.Value}");
            }

            Debug.WriteLine(command.CommandText);
            foreach (var p in command.Parameters.OfType<NpgsqlParameter>())
            {
                Debug.WriteLine($"  {p.ParameterName}: {p.Value}");
            }
        }

        public void LogFailure(NpgsqlCommand command, Exception ex)
        {
            _output.WriteLine("Postgresql command failed!");
            _output.WriteLine(command.CommandText);
            foreach (var p in command.Parameters.OfType<NpgsqlParameter>())
            {
                _output.WriteLine($"  {p.ParameterName}: {p.Value}");
            }
            _output.WriteLine(ex.ToString());
        }

        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
        {
            var lastCommit = commit;
            _output.WriteLine(
                $"Persisted {lastCommit.Updated.Count()} updates, {lastCommit.Inserted.Count()} inserts, and {lastCommit.Deleted.Count()} deletions");
        }

        public void OnBeforeExecute(NpgsqlCommand command)
        {

        }

        private class NoopTestOutputHelper : ITestOutputHelper
        {
            public void WriteLine(string message)
            {
            }

            public void WriteLine(string format, params object[] args)
            {
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                _output.WriteLine(exception?.ToString());
            }
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            throw new NotImplementedException();
        }
    }
}
