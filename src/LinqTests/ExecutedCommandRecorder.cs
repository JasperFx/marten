using System;
using System.Collections.Generic;
using System.Linq;
using Marten;
using Marten.Services;
using Npgsql;

namespace LinqTests;

/// <summary>
/// Records the SQL and the parameter values of every command a session actually sends, so a test can
/// assert on what executed rather than on what <c>ToCommand()</c> says would execute. Each batch command
/// is recorded on its own, with its parameters in position order -- <c>$1</c> is <c>Parameters[0]</c>.
/// </summary>
internal sealed class ExecutedCommandRecorder: IMartenSessionLogger
{
    public List<(string Sql, object[] Parameters)> Commands { get; } = new();

    public static object ParameterValue(NpgsqlParameter parameter)
    {
        return parameter.Value is byte[] bytes ? Convert.ToBase64String(bytes) : parameter.Value;
    }

    public void LogSuccess(NpgsqlCommand command) =>
        Commands.Add((command.CommandText, command.Parameters.Cast<NpgsqlParameter>().Select(ParameterValue).ToArray()));

    public void LogSuccess(NpgsqlBatch batch)
    {
        foreach (var command in batch.BatchCommands.Cast<NpgsqlBatchCommand>())
        {
            Commands.Add((command.CommandText, command.Parameters.Cast<NpgsqlParameter>().Select(ParameterValue).ToArray()));
        }
    }

    public void LogFailure(NpgsqlCommand command, Exception ex) { }
    public void LogFailure(NpgsqlBatch batch, Exception ex) { }
    public void LogFailure(Exception ex, string message) { }
    public void RecordSavedChanges(IDocumentSession session, IChangeSet commit) { }
    public void OnBeforeExecute(NpgsqlCommand command) { }
    public void OnBeforeExecute(NpgsqlBatch batch) { }
}
