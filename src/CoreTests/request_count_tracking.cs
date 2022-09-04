using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests;

public class request_count_tracking : IDisposable
{
    private readonly RecordingLogger logger = new();
    private readonly DocumentStore _store;
    private readonly IDocumentSession theSession;

    public request_count_tracking()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "request_counts";
        });

        theSession = _store.LightweightSession();
        theSession.Logger = logger;
    }

    public void Dispose()
    {
        _store?.Dispose();
        theSession?.Dispose();
    }

    [Fact]
    public async Task increments_the_request_count()
    {
        theSession.RequestCount.ShouldBe(0);

        theSession.Execute(new NpgsqlCommand("select 1"));
        theSession.RequestCount.ShouldBe(1);

        theSession.Execute(new NpgsqlCommand("select 2"));
        theSession.RequestCount.ShouldBe(2);

        theSession.Execute(new NpgsqlCommand("select 3"));
        theSession.RequestCount.ShouldBe(3);

        theSession.Execute(new NpgsqlCommand("select 4"));
        theSession.RequestCount.ShouldBe(4);

        await theSession.ExecuteAsync(new NpgsqlCommand("select 5"));
        theSession.RequestCount.ShouldBe(5);
    }

    [Fact]
    public async Task log_execute_failure_1_async()
    {
        var ex = await Should.ThrowAsync<MartenCommandException>(async () =>
        {
            await theSession.ExecuteAsync(new NpgsqlCommand("select foo from nonexistent"));
        });

        logger.OnBeforeExecuted.ShouldBe(1);

        logger.LastCommand.CommandText.ShouldBe("select foo from nonexistent");
        logger.LastException.ShouldBe(ex.InnerException);
    }


    [Fact]
    public void log_execute_failure_2()
    {

        var cmd = new NpgsqlCommand("select foo from nonexistent");

        var ex = Should.Throw<MartenCommandException>(() =>
            theSession.Execute(cmd));

        logger.LastCommand.ShouldBe(cmd);
        logger.LastException.ShouldBe(ex.InnerException);

    }


    [Fact]
    public void log_execute_success_1()
    {
        theSession.Execute(new NpgsqlCommand("select 1"));

        logger.LastCommand.CommandText.ShouldBe("select 1");

    }


    [Fact]
    public async Task log_execute_success_1_async()
    {
        await theSession.ExecuteAsync(new NpgsqlCommand("select 1"));

        logger.LastCommand.CommandText.ShouldBe("select 1");
    }


    [Fact]
    public void log_execute_success_2()
    {
        var cmd = new NpgsqlCommand("select 1");
        theSession.Execute(cmd);

        logger.LastCommand.ShouldBeSameAs(cmd);
    }


    [Fact]
    public async Task log_execute_success_2_async()
    {
        var cmd = new NpgsqlCommand("select 1");
        await theSession.ExecuteAsync(cmd);

        logger.LastCommand.ShouldBeSameAs(cmd);
    }
}

public class RecordingLogger: IMartenSessionLogger
{
    public NpgsqlCommand LastCommand;
    public Exception LastException;
    public readonly IList<IChangeSet> Commits = new List<IChangeSet>();
    public IChangeSet LastCommit { get; set; }

    public IDocumentSession LastSession { get; set; }

    public int OnBeforeExecuted { get; set; }

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

    public void LogSuccess(NpgsqlCommand command)
    {
        LastCommand = command;
    }

    public void LogFailure(NpgsqlCommand command, Exception ex)
    {
        LastCommand = command;
        LastException = ex;
    }
}