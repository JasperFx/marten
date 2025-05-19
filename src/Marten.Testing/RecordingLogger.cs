using System;
using System.Collections.Generic;
using Marten.Services;
using Npgsql;

namespace Marten.Testing;

public class RecordingLogger: IMartenSessionLogger
{
    public NpgsqlCommand LastCommand;
    public Exception LastException;
    public readonly IList<IChangeSet> Commits = new List<IChangeSet>();
    public IChangeSet LastCommit { get; set; }

    public IDocumentSession LastSession { get; set; }

    public int OnBeforeExecuted { get; set; }

    public void LogFailure(NpgsqlBatch batch, Exception ex)
    {

    }

    public void LogFailure(Exception ex, string message)
    {

    }

    public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
    {
        LastSession = session;
        LastCommit = commit.Clone();

        Commits.Add(commit);
    }

    public void OnBeforeExecute(NpgsqlCommand command)
    {
        OnBeforeExecuted++;
    }

    public void OnBeforeExecute(NpgsqlBatch batch)
    {

    }

    public void LogSuccess(NpgsqlCommand command)
    {
        LastCommand = command;
    }

    public void LogFailure(NpgsqlCommand command, Exception ex)
    {
        LastCommand = command;
        LastException = ex;
    }

    public void LogSuccess(NpgsqlBatch batch)
    {

    }
}
