using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Testing.Harness;
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

    private class SerializerBlewUp(string message) : Exception(message);

    private class OperationThatFailsToConfigure : IStorageOperation
    {
        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            // Half an upsert, as a serializer failing on a parameter leaves it.
            builder.Append("select mt_upsert_something(");
            throw new SerializerBlewUp("the serializer could not write this document");
        }

        public Type DocumentType => typeof(string);

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
}
