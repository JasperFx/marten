using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DocumentDbTests.SessionMechanics;

public class document_session_logs_SaveChanges : IntegrationContext
{
    [Fact]
    public async Task records_on_SaveChanges()
    {
        var logger = new RecordingLogger();

        theSession.Logger = logger;

        theSession.Store(Target.Random());

        await theSession.SaveChangesAsync();

        logger.LastSession.ShouldBe(theSession);
    }

    [Fact]
    public async Task records_on_SaveChangesAsync()
    {
        var logger = new RecordingLogger();

        theSession.Logger = logger;

        theSession.Store(Target.Random());

        await theSession.SaveChangesAsync();

        logger.LastSession.ShouldBe(theSession);
    }

    public document_session_logs_SaveChanges(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}

public class RecordingLogger : IMartenSessionLogger
{
    public readonly IList<IChangeSet> Commits = new List<IChangeSet>();

    public void LogSuccess(DbCommand command)
    {

    }

    public void LogFailure(DbCommand command, Exception ex)
    {
    }

    public void LogSuccess(DbBatch batch)
    {

    }

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

    }

    public void OnBeforeExecute(DbBatch batch)
    {

    }

    public IChangeSet LastCommit { get; set; }

    public IDocumentSession LastSession { get; set; }
}
