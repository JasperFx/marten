using System;
using System.Collections.Generic;
using System.Data.Common;
using Marten.Services;
using Npgsql;
using Weasel.Core.Operations;

namespace Marten.Testing.Examples;

public class RecordingLogger: IMartenSessionLogger
{
    public readonly List<DbCommand> Commands = new();

    public void LogSuccess(DbCommand command)
    {
        Commands.Add(command);
    }

    public void LogFailure(DbCommand command, Exception ex)
    {
        Commands.Add(command);
    }

    public void LogSuccess(DbBatch batch)
    {
        foreach (var command in batch.BatchCommands)
        {
            Commands.Add(new NpgsqlCommand(command.CommandText));
        }
    }

    public void LogFailure(DbBatch batch, Exception ex)
    {
    }

    public void LogFailure(Exception ex, string message)
    {

    }

    public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
    {
        // do nothing
    }

    public void OnBeforeExecute(DbCommand command)
    {

    }

    public void OnBeforeExecute(DbBatch batch)
    {

    }
}
