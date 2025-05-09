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
    private long? _timestamp;
    private readonly DefaultLoggingWriter _loggerOutput;

    public DefaultMartenLogger(ILogger inner)
    {
        Inner = inner;
        _loggerOutput = new DefaultLoggingWriter(inner);
    }

    public ILogger Inner { get; }

    public IMartenSessionLogger StartSession(IQuerySession session)
    {
        return this;
    }

    public void SchemaChange(string sql)
    {
        _loggerOutput.SchemaChange(sql);
    }

    public void LogSuccess(NpgsqlCommand command)
    {
        if (Inner.IsEnabled(LogLevel.Debug))
        {
            var duration = Stopwatch.GetElapsedTime(_timestamp!.Value);
            var parameters = command.Parameters
                .Select(p => $"  {p.ParameterName}: {p.Value}")
                .Join(Environment.NewLine);
            _loggerOutput.LogSuccess(duration.TotalMilliseconds, command.CommandText, parameters);
        }
    }

    public void LogSuccess(NpgsqlBatch batch)
    {
        if (Inner.IsEnabled(LogLevel.Debug))
        {
            var duration = Stopwatch.GetElapsedTime(_timestamp!.Value);
            foreach (var command in batch.BatchCommands)
            {
                var parameters = command.Parameters.OfType<NpgsqlParameter>()
                    .Select(p => $"  {p.ParameterName}: {p.Value}")
                    .Join(Environment.NewLine);
                _loggerOutput.LogSuccess(duration.TotalMilliseconds, command.CommandText, parameters);
            }
        }
    }

    public void LogFailure(NpgsqlCommand command, Exception ex)
    {
        var parameters = command.Parameters
            .Select(p => $"  {p.ParameterName}: {p.Value}")
            .Join(Environment.NewLine);

        _loggerOutput.LogError(ex, command.CommandText, parameters);
    }

    public void LogFailure(NpgsqlBatch batch, Exception ex)
    {
        foreach (var command in batch.BatchCommands)
        {
            var parameters = command.Parameters.OfType<NpgsqlParameter>()
                .Select(p => $"  {p.ParameterName}: {p.Value}")
                .Join(Environment.NewLine);

            _loggerOutput.LogError(ex, command.CommandText, parameters);
        }
    }

    public void LogFailure(Exception ex, string message)
    {
        _loggerOutput.LogError(ex, message);
    }

    public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
    {
        if (Inner.IsEnabled(LogLevel.Debug))
        {
            var duration = Stopwatch.GetElapsedTime(_timestamp!.Value);
            _loggerOutput.RecordSavedChanges(commit.Updated.Count(),
                duration.TotalMilliseconds,
                commit.Inserted.Count(),
                commit.Deleted.Count());
        }
    }

    public void OnBeforeExecute(NpgsqlCommand command)
    {
        if (Inner.IsEnabled(LogLevel.Debug))
        {
            _timestamp = Stopwatch.GetTimestamp();
        }
    }

    public void OnBeforeExecute(NpgsqlBatch batch)
    {
        if (Inner.IsEnabled(LogLevel.Debug))
        {
            _timestamp = Stopwatch.GetTimestamp();
        }
    }
}

internal sealed partial class DefaultLoggingWriter
{
    private readonly ILogger _logger;

    public DefaultLoggingWriter(ILogger logger)
    {
        _logger = logger;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Executed schema update SQL:\n{SQL}")]
    public partial void SchemaChange(string sql);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Marten executed in {milliseconds} ms, SQL: {SQL}\n{PARAMETERS}",
        SkipEnabledCheck = true)]
    public partial void LogSuccess(double milliseconds, string sql, string parameters);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Marten encountered an exception executing \n{SQL}\n{PARAMETERS}")]
    public partial void LogError(Exception ex, string sql, string parameters);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Marten encountered an exception with message: {MESSAGE}")]
    public partial void LogError(Exception ex, string message);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message =  "Persisted {UpdateCount} updates in {ElapsedMilliseconds} ms, {InsertedCount} inserts, and {DeletedCount} deletions",
        SkipEnabledCheck = true)]
    public partial void RecordSavedChanges(int updateCount, double elapsedMilliseconds, int insertedCount, int deletedCount);

}


