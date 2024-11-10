using System;
using System.Collections.Generic;
using System.Data.Common;
using Marten.Services;
using Npgsql;
using Weasel.Core.Operations;

namespace Marten.Testing;

public class RecordingLogger: IMartenSessionLogger
{
    public DbCommand LastCommand;
    public Exception LastException;
    public readonly IList<IChangeSet> Commits = new List<IChangeSet>();
    public IChangeSet LastCommit { get; set; }

    public IDocumentSession LastSession { get; set; }

    public int OnBeforeExecuted { get; set; }

    public void LogFailure(DbBatch batch, Exception ex)
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

    public void OnBeforeExecute(DbCommand command)
    {
        OnBeforeExecuted++;
    }

    public void OnBeforeExecute(DbBatch batch)
    {

    }

    public void LogSuccess(DbCommand command)
    {
        LastCommand = command;
    }

    public void LogFailure(DbCommand command, Exception ex)
    {
        LastCommand = command;
        LastException = ex;
    }

    public void LogSuccess(DbBatch batch)
    {

    }
}
