using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using Marten;
using Marten.Events.Daemon.Internals;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Storage;
using Xunit;
using ICommandBuilder = Weasel.Postgresql.ICommandBuilder;

namespace DaemonTests.Bugs;

/// <summary>
/// marten#5497 — when an operation throws while it is appended to the batch (a document serializer that
/// fails in WriteToParameter, for example), the exception was logged by the operation queue and dropped.
/// The page kept the half-configured command, so the batch later failed on unrelated SQL
/// ("42883: operator does not exist: text = integer"), or committed without the document.
/// </summary>
public class Bug_5497_an_operation_that_cannot_be_configured_faults_the_batch : OneOffConfigurationsContext
{
    [Fact]
    public async Task the_original_exception_surfaces_from_the_batch()
    {
        using var cts = new CancellationTokenSource(30.Seconds());
        var session = (DocumentSessionBase)theStore.LightweightSession();
        await using var batch = new ProjectionUpdateBatch(
            theStore.Options.Projections, session, ShardExecutionMode.Continuous, cts.Token);

        batch.Queue.Post(new OperationThatFailsToConfigure());
        batch.Queue.Post(new HarmlessOperation());

        var thrown = await Should.ThrowAsync<SerializerBlewUp>(() => batch.WaitForCompletion());
        thrown.Message.ShouldBe("the serializer could not write this document");
    }

    [Fact]
    public async Task an_operation_after_the_failure_is_not_added_to_a_page()
    {
        using var cts = new CancellationTokenSource(30.Seconds());
        var session = (DocumentSessionBase)theStore.LightweightSession();
        await using var batch = new ProjectionUpdateBatch(
            theStore.Options.Projections, session, ShardExecutionMode.Continuous, cts.Token);

        var harmless = new HarmlessOperation();
        batch.Queue.Post(new OperationThatFailsToConfigure());
        batch.Queue.Post(harmless);

        await Should.ThrowAsync<SerializerBlewUp>(() => batch.WaitForCompletion());
        harmless.Configured.ShouldBeFalse();
    }

    [Fact]
    public async Task the_failure_names_the_operation_on_the_exception()
    {
        using var cts = new CancellationTokenSource(30.Seconds());
        var session = (DocumentSessionBase)theStore.LightweightSession();
        await using var batch = new ProjectionUpdateBatch(
            theStore.Options.Projections, session, ShardExecutionMode.Continuous, cts.Token);

        batch.Queue.Post(new OperationThatFailsToConfigure());

        var thrown = await Should.ThrowAsync<SerializerBlewUp>(() => batch.WaitForCompletion());

        // A serializer failure says nothing about what it was writing, so the batch says it instead
        var description = thrown.Data[ProjectionUpdateBatch.FailedOperationDataKey].ShouldBeOfType<string>();
        description.ShouldContain(nameof(OperationThatFailsToConfigure));
        description.ShouldContain(nameof(OperationRole.Upsert));
        description.ShouldContain(nameof(DocumentTheSerializerHated));
    }

    [Fact]
    public async Task the_failure_is_logged_against_the_session()
    {
        using var cts = new CancellationTokenSource(30.Seconds());
        var session = (DocumentSessionBase)theStore.LightweightSession();
        var logger = new FailureRecordingLogger();
        session.Logger = logger;

        await using var batch = new ProjectionUpdateBatch(
            theStore.Options.Projections, session, ShardExecutionMode.Continuous, cts.Token);

        batch.Queue.Post(new OperationThatFailsToConfigure());

        var thrown = await Should.ThrowAsync<SerializerBlewUp>(() => batch.WaitForCompletion());

        var failure = logger.Failures.ShouldHaveSingleItem();
        failure.Exception.ShouldBeSameAs(thrown);
        failure.Message.ShouldContain(nameof(OperationThatFailsToConfigure));
        failure.Message.ShouldContain(nameof(DocumentTheSerializerHated));
    }

    [Fact]
    public async Task a_logger_that_throws_does_not_replace_the_real_failure()
    {
        using var cts = new CancellationTokenSource(30.Seconds());
        var session = (DocumentSessionBase)theStore.LightweightSession();
        session.Logger = new ThrowingLogger();

        await using var batch = new ProjectionUpdateBatch(
            theStore.Options.Projections, session, ShardExecutionMode.Continuous, cts.Token);

        batch.Queue.Post(new OperationThatFailsToConfigure());

        await Should.ThrowAsync<SerializerBlewUp>(() => batch.WaitForCompletion());
    }

    private class SerializerBlewUp(string message) : Exception(message);

    private class DocumentTheSerializerHated;

    private class OperationThatFailsToConfigure : IStorageOperation
    {
        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            // Half an upsert, as a serializer failing on a parameter leaves it.
            builder.Append("select mt_upsert_something(");
            throw new SerializerBlewUp("the serializer could not write this document");
        }

        public Type DocumentType => typeof(DocumentTheSerializerHated);

        public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token) =>
            Task.CompletedTask;

        public OperationRole Role() => OperationRole.Upsert;
    }

    private class HarmlessOperation : IStorageOperation
    {
        public bool Configured { get; private set; }

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            Configured = true;
            builder.Append("select 1");
        }

        public Type DocumentType => typeof(string);

        public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token) =>
            Task.CompletedTask;

        public OperationRole Role() => OperationRole.Other;
    }

    private abstract class StubSessionLogger : IMartenSessionLogger
    {
        public abstract void LogFailure(Exception ex, string message);

        public void LogSuccess(NpgsqlCommand command) { }
        public void LogSuccess(NpgsqlBatch batch) { }
        public void LogFailure(NpgsqlCommand command, Exception ex) { }
        public void LogFailure(NpgsqlBatch batch, Exception ex) { }
        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit) { }
        public void OnBeforeExecute(NpgsqlCommand command) { }
        public void OnBeforeExecute(NpgsqlBatch batch) { }
    }

    private class FailureRecordingLogger : StubSessionLogger
    {
        public List<(Exception Exception, string Message)> Failures { get; } = new();

        public override void LogFailure(Exception ex, string message) => Failures.Add((ex, message));
    }

    private class ThrowingLogger : StubSessionLogger
    {
        public override void LogFailure(Exception ex, string message) =>
            throw new InvalidOperationException("this logger is broken too");
    }
}
