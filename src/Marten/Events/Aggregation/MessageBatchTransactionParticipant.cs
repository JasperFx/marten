#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Events.Aggregation;

/// <summary>
/// Adapts an <see cref="IMessageBatch" /> onto <see cref="ITransactionParticipant" /> so that its
/// before-commit hook fires <em>inside</em> Marten's write transaction, after every operation in
/// the unit of work has executed successfully and immediately before the <c>COMMIT</c>.
/// </summary>
/// <remarks>
/// <para>
/// #5353. The hook used to be invoked straight from <c>SaveChangesAsync</c> before the
/// <c>UpdateBatch</c> was even built -- so it ran before the transaction was open and before a
/// single statement had been attempted. A unit of work that then failed (a stream id collision, a
/// concurrency violation, any SQL error at all) left the outbox batch enlisted with no way to
/// un-enlist it: the batch had been told the transaction was about to commit, and was never told
/// that it did not.
/// </para>
/// <para>
/// Running it here instead gives the hook the meaning its name promises, and makes it unreachable
/// on the failure path -- an outbox that persists rows in the before hook still gets them in the
/// same transaction as the events that produced them, and an outbox that only flushes in the after
/// hook is untouched. It also matches the placement Polecat and Fisher already use, which is what
/// the shared <c>ProjectionSideEffectCompliance</c> suite asserts.
/// </para>
/// <para>
/// The change set is resolved lazily because the daemon builds its own from the batch's pages,
/// which are not final until the batch flushes.
/// </para>
/// <para>
/// One consequence worth naming: <c>AmbientTransactionLifetime</c> does not run transaction
/// participants at all -- it has no <see cref="NpgsqlTransaction" /> to hand one, because the
/// ambient <c>TransactionScope</c> owns the commit -- so a session enlisted in an ambient
/// transaction gets the after hook and not the before hook. That is the same pre-existing gap EF
/// Core participants already have there, and it is the honest answer: Marten cannot offer a
/// "before MY commit" hook for a commit it does not perform.
/// </para>
/// </remarks>
internal sealed class MessageBatchTransactionParticipant: ITransactionParticipant
{
    private readonly IMessageBatch _batch;
    private readonly IDocumentSession _session;
    private readonly Func<IChangeSet> _commit;

    public MessageBatchTransactionParticipant(IMessageBatch batch, IDocumentSession session,
        Func<IChangeSet> commit)
    {
        _batch = batch;
        _session = session;
        _commit = commit;
    }

    internal IMessageBatch Batch => _batch;

    public Task BeforeCommitAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken token)
    {
        return _batch.BeforeCommitAsync(_session, _commit(), token);
    }
}
