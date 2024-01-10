using System;
using System.Collections.Generic;
using Marten.Services;
using Npgsql;

namespace Marten.Testing.Examples;

public class RecordingLogger: IMartenSessionLogger
{
    public readonly IList<NpgsqlCommand> Commands = new List<NpgsqlCommand>();

    public void LogSuccess(NpgsqlCommand command)
    {
        Commands.Add(command);
    }

    public void LogFailure(NpgsqlCommand command, Exception ex)
    {
        Commands.Add(command);
    }

    public void LogSuccess(NpgsqlBatch batch)
    {
        foreach (var command in batch.BatchCommands)
        {
            Commands.Add(new NpgsqlCommand(command.CommandText));
        }
    }

    public void LogFailure(NpgsqlBatch batch, Exception ex)
    {
    }

    public void LogFailure(Exception ex, string message)
    {

    }

    public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
    {
        // do nothing
    }

    public void OnBeforeExecute(NpgsqlCommand command)
    {

    }

    public void OnBeforeExecute(NpgsqlBatch batch)
    {

    }
}
