using System;
using System.Diagnostics;
using System.Linq;
using Baseline;
using Marten.Services;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Marten
{
    internal class DefaultMartenLogger: IMartenLogger, IMartenSessionLogger
    {
        private readonly ILogger _logger;
        private Stopwatch _stopwatch;

        public DefaultMartenLogger(ILogger logger)
        {
            _logger = logger;
        }

        public IMartenSessionLogger StartSession(IQuerySession session)
        {
            return this;
        }

        public void SchemaChange(string sql)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Executed schema update SQL:\n{SQL}", sql);
            }
        }

        public void LogSuccess(NpgsqlCommand command)
        {
            _stopwatch?.Stop();

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Marten executed in {milliseconds} ms, SQL: {SQL}", _stopwatch?.ElapsedMilliseconds ?? 0 ,command.CommandText);

                foreach (NpgsqlParameter p in command.Parameters)
                {
                    _logger.LogDebug("    {ParameterName}: {ParameterValue}", p.ParameterName, p.Value);
                }
            }
        }

        public void LogFailure(NpgsqlCommand command, Exception ex)
        {
            _stopwatch?.Stop();

            var message = "Marten encountered an exception executing \n{SQL}\n" + command.Parameters
                .OfType<NpgsqlParameter>()
                .Select(p => $"  {p.ParameterName}: {p.Value}")
                .Join(Environment.NewLine);
            _logger.LogError(ex, message, command.CommandText);

        }

        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
        {
            _stopwatch?.Stop();

            var lastCommit = commit;
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    $"Persisted {lastCommit.Updated.Count()} updates in {_stopwatch?.ElapsedMilliseconds ?? 0} ms, {lastCommit.Inserted.Count()} inserts, and {lastCommit.Deleted.Count()} deletions");
            }
        }

        public void OnBeforeExecute(NpgsqlCommand command)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _stopwatch = new Stopwatch();
                _stopwatch.Start();
            }
        }
    }
}
