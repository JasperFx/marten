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
                var message = "Marten executed in {milliseconds} ms, SQL: {SQL}\n{PARAMS}";
                var parameters = command.Parameters.OfType<NpgsqlParameter>()
                    .Select(p => $"  {p.ParameterName}: {p.Value}")
                    .Join(Environment.NewLine);
                _logger.LogDebug(message, _stopwatch?.ElapsedMilliseconds ?? 0, command.CommandText, parameters);
            }
        }

        public void LogFailure(NpgsqlCommand command, Exception ex)
        {
            _stopwatch?.Stop();

            var message = "Marten encountered an exception executing \n{SQL}\n{PARAMS}";
            var parameters = command.Parameters.OfType<NpgsqlParameter>()
                .Select(p => $"  {p.ParameterName}: {p.Value}")
                .Join(Environment.NewLine);
            _logger.LogError(ex, message, command.CommandText, parameters);

        }

        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
        {
            _stopwatch?.Stop();

            var lastCommit = commit;
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Persisted {UpdateCount} updates in {ElapsedMilliseconds} ms, {InsertedCount} inserts, and {DeletedCount} deletions",
                    lastCommit.Updated.Count(), _stopwatch?.ElapsedMilliseconds ?? 0, lastCommit.Inserted.Count(), lastCommit.Deleted.Count());
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
