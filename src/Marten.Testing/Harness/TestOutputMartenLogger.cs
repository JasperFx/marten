using System;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Marten.Services;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit.Abstractions;

namespace Marten.Testing.Harness
{
    public class TestOutputMartenLogger : IMartenLogger, IMartenSessionLogger, ILogger
    {
        private readonly ITestOutputHelper _output;
        private static readonly ITestOutputHelper _noopTestOutputHelper = new NoopTestOutputHelper();

        private readonly StringWriter _writer = new StringWriter();

        public TestOutputMartenLogger(ITestOutputHelper output)
        {
            _output = output ?? _noopTestOutputHelper;
        }

        public IMartenSessionLogger StartSession(IQuerySession session)
        {
            return this;
        }

        public string ExecutedSql()
        {
            return _writer.ToString();
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

        public void LogSuccess(DbCommand command)
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

            _writer.WriteLine(command.CommandText);
        }

        public void LogSuccess(DbBatch batch)
        {
            foreach (var command in batch.BatchCommands)
            {
                _output.WriteLine(command.CommandText);
                int position = 0;
                foreach (var p in command.Parameters.OfType<NpgsqlParameter>())
                {
                    position++;
                    _output.WriteLine($"  ${position}: {p.Value}");
                }

                Debug.WriteLine(command.CommandText);
                position = 0;
                foreach (var p in command.Parameters.OfType<NpgsqlParameter>())
                {
                    position++;
                    Debug.WriteLine($"  ${position}: {p.Value}");
                }

                _writer.WriteLine(command.CommandText);
            }
        }

        public void LogFailure(DbCommand command, Exception ex)
        {
            _output.WriteLine("Postgresql command failed!");
            _output.WriteLine(command.CommandText);
            foreach (var p in command.Parameters.OfType<NpgsqlParameter>())
            {
                _output.WriteLine($"  {p.ParameterName}: {p.Value}");
            }
            _output.WriteLine(ex.ToString());
        }

        public void LogFailure(DbBatch batch, Exception ex)
        {
            _output.WriteLine("Postgresql command failed!");

            foreach (var command in batch.BatchCommands)
            {
                _output.WriteLine(command.CommandText);
                int position = 0;
                foreach (var p in command.Parameters.OfType<NpgsqlParameter>())
                {
                    position++;
                    _output.WriteLine($"  ${position}: {p.Value}");
                }
            }

            _output.WriteLine(ex.ToString());
        }

        public void LogFailure(Exception ex, string message)
        {
            _output.WriteLine("Failure: " + message);
            _output.WriteLine(ex.ToString());
        }

        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
        {
            var lastCommit = commit;
            _output.WriteLine(
                $"Persisted {lastCommit.Updated.Count()} updates, {lastCommit.Inserted.Count()} inserts, and {lastCommit.Deleted.Count()} deletions");
        }

        public void OnBeforeExecute(DbCommand command)
        {

        }

        public void OnBeforeExecute(DbBatch batch)
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
