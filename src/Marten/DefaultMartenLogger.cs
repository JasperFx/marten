using System;
using System.Diagnostics;
using System.Linq;
using JasperFx.Core;
using Marten.Services;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Marten;

internal class DefaultMartenLogger: IMartenLogger, IMartenSessionLogger
{
    private Stopwatch _stopwatch;

    public DefaultMartenLogger(ILogger inner)
    {
        Inner = inner;
    }

    public ILogger Inner { get; }

    public IMartenSessionLogger StartSession(IQuerySession session)
    {
        return this;
    }

    public void SchemaChange(string sql)
    {
        if (Inner.IsEnabled(LogLevel.Information))
        {
            Inner.LogInformation("Executed schema update SQL:\n{SQL}", sql);
        }
    }

    public void LogSuccess(NpgsqlCommand command)
    {
        _stopwatch?.Stop();

        if (Inner.IsEnabled(LogLevel.Debug))
        {
            var message = "Marten executed in {milliseconds} ms, SQL: {SQL}\n{PARAMS}";
            var parameters = command.Parameters.OfType<NpgsqlParameter>()
                .Select(p => $"  {p.ParameterName}: {p.Value}")
                .Join(Environment.NewLine);
            Inner.LogDebug(message, _stopwatch?.ElapsedMilliseconds ?? 0, command.CommandText, parameters);
        }
    }

    public void LogSuccess(NpgsqlBatch batch)
    {
        if (Inner.IsEnabled(LogLevel.Debug))
        {
            foreach (var command in batch.BatchCommands)
            {
                var message = "Marten executed, SQL: {SQL}\n{PARAMS}";
                var parameters = command.Parameters.OfType<NpgsqlParameter>()
                    .Select(p => $"  {p.ParameterName}: {p.Value}")
                    .Join(Environment.NewLine);
                Inner.LogDebug(message, _stopwatch?.ElapsedMilliseconds ?? 0, command.CommandText, parameters);
            }
        }
    }

    public void LogFailure(NpgsqlCommand command, Exception ex)
    {
        _stopwatch?.Stop();

        var message = "Marten encountered an exception executing \n{SQL}\n{PARAMS}";
        var parameters = command.Parameters.OfType<NpgsqlParameter>()
            .Select(p => $"  {p.ParameterName}: {p.Value}")
            .Join(Environment.NewLine);
        Inner.LogError(ex, message, command.CommandText, parameters);
    }

    public void LogFailure(NpgsqlBatch batch, Exception ex)
    {
        _stopwatch?.Stop();

        var message = "Marten encountered an exception executing \n{SQL}\n{PARAMS}";

        foreach (var command in batch.BatchCommands)
        {
            var parameters = command.Parameters.OfType<NpgsqlParameter>()
                .Select(p => $"  {p.ParameterName}: {p.Value}")
                .Join(Environment.NewLine);
            Inner.LogError(ex, message, command.CommandText, parameters);
        }
    }

    public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
    {
        _stopwatch?.Stop();

        var lastCommit = commit;
        if (Inner.IsEnabled(LogLevel.Debug))
        {
            Inner.LogDebug(
                "Persisted {UpdateCount} updates in {ElapsedMilliseconds} ms, {InsertedCount} inserts, and {DeletedCount} deletions",
                lastCommit.Updated.Count(), _stopwatch?.ElapsedMilliseconds ?? 0, lastCommit.Inserted.Count(),
                lastCommit.Deleted.Count());
        }
    }

    public void OnBeforeExecute(NpgsqlCommand command)
    {
        if (Inner.IsEnabled(LogLevel.Debug))
        {
            _stopwatch = new Stopwatch();
            _stopwatch.Start();
        }
    }

    public void LogFailure(Exception ex, string message)
    {
        Inner.LogError(ex, message);
    }

    public void OnBeforeExecute(NpgsqlBatch batch)
    {
        if (Inner.IsEnabled(LogLevel.Debug))
        {
            _stopwatch = new Stopwatch();
            _stopwatch.Start();
        }
    }
}
