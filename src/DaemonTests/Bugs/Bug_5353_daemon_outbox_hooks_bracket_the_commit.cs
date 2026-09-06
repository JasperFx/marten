using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.Aggregations;
using JasperFx.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Internal.Sessions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

/// <summary>
/// marten#5353 — the async daemon's half of "a failed unit of work still commits its outbox batch".
/// </summary>
/// <remarks>
/// <para>
/// The daemon used to add its <c>IMessageBatch</c> to <see cref="ProjectionUpdateBatch.Listeners" />,
/// which meant <see cref="ProjectionUpdateBatch.PreUpdateAsync" /> fired the batch's before-commit
/// hook before a single one of the batch's pages had been executed. A daemon batch that then failed
/// had already told its outbox the transaction was about to commit, and there is no rollback hook on
/// <c>IChangeListener</c> to take that back — the same defect the inline
/// <c>SaveChangesAsync</c> path had, and the reason the compliance fact
/// <c>a_unit_of_work_that_fails_publishes_nothing</c> was red.
/// </para>
/// <para>
/// The batch is now enlisted as an <c>ITransactionParticipant</c>, which the connection lifetime runs
/// after every page has succeeded and immediately before the <c>COMMIT</c>, so a batch that fails
/// never reaches it. The after-commit hook still fires from <c>PostUpdateAsync</c>.
/// <c>side_effects_in_aggregations.publishing_messages_in_continuous_mode</c> is the end-to-end
/// positive control that both hooks still fire on a batch that does commit.
/// </para>
/// </remarks>
public class Bug_5353_daemon_outbox_hooks_bracket_the_commit: OneOffConfigurationsContext
{
    [Fact]
    public async Task the_before_commit_hook_is_not_fired_before_the_batch_executes()
    {
        var outbox = new RecordingMessageOutbox();
        StoreOptions(opts => opts.Events.MessageOutbox = outbox);

        var session = (DocumentSessionBase)theStore.LightweightSession();
        await using var batch = new ProjectionUpdateBatch(theStore.Options.Projections, session,
            ShardExecutionMode.Continuous, CancellationToken.None) { ShouldApplyListeners = true };

        await batch.SpinUpMessageBatchAsync(session);

        var messageBatch = outbox.Batches.ShouldHaveSingleItem();

        // The pre-execution listener pass runs before any of this batch's SQL has been attempted, so
        // it must no longer reach the outbox.
        await batch.PreUpdateAsync(session);
        messageBatch.BeforeCommitWasCalled.ShouldBeFalse(
            "the outbox batch must not be told about a commit that has not been attempted yet");

        // It is enlisted as a transaction participant instead. The connection lifetime invokes these
        // inside the transaction, after every page has succeeded and immediately before the COMMIT.
        var participant = batch.TransactionParticipants.ShouldHaveSingleItem();
        await participant.BeforeCommitAsync(null!, null!, CancellationToken.None);
        messageBatch.BeforeCommitWasCalled.ShouldBeTrue();

        // ...and the after hook still fires even though the batch is no longer a Listener.
        await batch.PostUpdateAsync(session);
        messageBatch.AfterCommitWasCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task no_participant_is_enlisted_when_commit_hooks_are_suppressed()
    {
        // ShouldApplyListeners is false for rebuilds and for batches carrying no events; the
        // listener path already gated both hooks on it, and the participant path has to keep that
        // gate or a rebuild would start firing commit hooks it never used to fire.
        var outbox = new RecordingMessageOutbox();
        StoreOptions(opts => opts.Events.MessageOutbox = outbox);

        var session = (DocumentSessionBase)theStore.LightweightSession();
        await using var batch = new ProjectionUpdateBatch(theStore.Options.Projections, session,
            ShardExecutionMode.Rebuild, CancellationToken.None) { ShouldApplyListeners = false };

        await batch.SpinUpMessageBatchAsync(session);

        batch.TransactionParticipants.Any().ShouldBeFalse();

        await batch.PreUpdateAsync(session);
        await batch.PostUpdateAsync(session);

        var messageBatch = outbox.Batches.ShouldHaveSingleItem();
        messageBatch.BeforeCommitWasCalled.ShouldBeFalse();
        messageBatch.AfterCommitWasCalled.ShouldBeFalse();
    }
}
