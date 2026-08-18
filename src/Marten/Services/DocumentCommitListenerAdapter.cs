#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Documents;

namespace Marten.Services;

/// <summary>
/// Forwards Marten's post-commit session callback onto the store-agnostic
/// <see cref="IDocumentCommitListener" /> (jasperfx#679).
/// </summary>
/// <remarks>
/// <para>
/// An adapter rather than an interface added to <see cref="DocumentSessionListenerBase" />, and
/// unlike jasperfx#669/#673 that is forced by the compiler rather than chosen. Marten's
/// <c>AfterCommitAsync(IDocumentSession, IChangeSet, CancellationToken)</c> differs from the
/// contract's <c>AfterCommitAsync(IDocumentSessionOperations, IDocumentChangeSet,
/// CancellationToken)</c> in three of its four positions, and neither contract member has a default
/// implementation — so a store type declaring <see cref="IDocumentCommitListener" /> on top of the
/// existing method gets CS0535, not a silent bind to a throwing default. What the compiler still
/// cannot see is the wiring, which is why <c>DocumentCommitListenerCompliance</c> rather than a
/// green build is the evidence this works.
/// </para>
/// <para>
/// Nothing else about Marten's listener pipeline is adapted. <c>BeforeSaveChangesAsync</c>,
/// <c>DocumentLoaded</c> and <c>DocumentAddedForStorage</c> stay on the base class's no-ops because
/// the shared contract is deliberately post-commit only — a pre-commit hook wanting to read what is
/// about to be appended is served by <c>IDocumentSessionOperations.PendingStreams</c> (jasperfx#673)
/// instead.
/// </para>
/// <para>
/// ⚠️ This is the SESSION half only. It does not fire for the async daemon's projection batches:
/// <c>ProjectionUpdateBatch</c> runs its own <see cref="IChangeListener" /> loop, and JasperFx owns
/// that half separately as <c>IDaemonChangeListener</c>. A consumer wanting both registers both.
/// </para>
/// </remarks>
internal sealed class DocumentCommitListenerAdapter: DocumentSessionListenerBase
{
    public DocumentCommitListenerAdapter(IDocumentCommitListener inner)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>
    /// The listener this adapter wraps. Exposed so that a second registration of the same listener
    /// instance can be detected, and so tests can assert on what was registered rather than on the
    /// wrapper.
    /// </summary>
    public IDocumentCommitListener Inner { get; }

    public override Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        // IDocumentSession derives from IDocumentSessionOperations (#5216), so the session goes
        // across as-is with no wrapper of its own.
        return Inner.AfterCommitAsync(session, new MartenDocumentChangeSet(commit), token);
    }
}

/// <summary>
/// A materialized snapshot of one Marten commit, as <see cref="IDocumentChangeSet" />.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <strong>The three collections are copied in the constructor, and that is load-bearing rather
/// than defensive.</strong> Marten's <see cref="IChangeSet" /> <em>is</em> the session's live
/// <c>UnitOfWork</c>: its <c>Inserted</c>, <c>Updated</c> and <c>Deleted</c> are lazy LINQ chains
/// re-walked over <c>_operations</c> on every read, and
/// <c>DocumentSessionBase.SaveChangesAsync</c> calls <c>_workTracker.Reset()</c> immediately after
/// the listener loop returns. A forward that deferred — <c>=&gt; inner.Inserted.ToList()</c> on a
/// property, let alone handing the sequences straight over — answers correctly inside the callback
/// and empty to a listener that stashed the change set and read it later. That is exactly what
/// <c>DocumentCommitListenerCompliance.the_change_set_survives_the_session_moving_on</c> exists to
/// catch, and it is why the shared contract is declared in <see cref="IReadOnlyList{T}" /> rather
/// than <see cref="IEnumerable{T}" />.
/// </para>
/// <para>
/// Copying once here is also why nothing calls <see cref="IChangeSet.Clone" />: with the snapshot
/// taken there is no live object left to defend against. A single enumeration per collection is
/// additionally the cheaper shape — the compliance suite's own
/// <c>Inserted.Concat(Updated)</c> would otherwise re-walk the operation list twice per read.
/// </para>
/// <para>
/// <c>GetEvents()</c> and <c>GetStreams()</c> have no counterpart on the shared contract and are
/// deliberately not projected here; jasperfx#679 shipped the measured surface, and they can be added
/// additively upstream when a consumer needs them.
/// </para>
/// </remarks>
internal sealed class MartenDocumentChangeSet: IDocumentChangeSet
{
    public MartenDocumentChangeSet(IChangeSet inner)
    {
        Inserted = inner.Inserted.ToArray();
        Updated = inner.Updated.ToArray();

        // Descriptors rather than the deleted documents themselves. Marten's Weasel.Storage.IDeletion
        // does carry a Document, but Polecat's and Fisher's change sets carry only { DocumentType, Id },
        // so the shared contract cannot expose one -- and Delete<T>(id) / DeleteWhere<T>(...) never
        // loaded a document to report in the first place.
        Deleted = inner.Deleted
            .Select(x => (IDocumentDeletion)new MartenDocumentDeletion(x.DocumentType, x.Id))
            .ToArray();
    }

    public IReadOnlyList<object> Inserted { get; }
    public IReadOnlyList<object> Updated { get; }
    public IReadOnlyList<IDocumentDeletion> Deleted { get; }
}

/// <summary>
/// One document removed by a committed transaction, as <see cref="IDocumentDeletion" />.
/// </summary>
/// <remarks>
/// A wrapper rather than <c>Weasel.Storage.IDeletion : IDocumentDeletion</c>, for two reasons.
/// Structurally the members already line up — <c>DocumentType</c> comes from
/// <c>Weasel.Core.IStorageOperation</c> and <c>Id</c> is declared on <c>IDeletion</c> itself — but C#
/// has no structural implementation, and putting the JasperFx interface on a Weasel type would make
/// Weasel depend on JasperFx.Events. The contract owns <see cref="IDocumentDeletion" /> precisely so
/// that a consumer reading a commit never has to name Weasel.
/// </remarks>
/// <param name="DocumentType">The document type that was deleted.</param>
/// <param name="Id">
/// The identity of the deleted document. Declared nullable to match the contract: a deletion
/// expressed as criteria (<c>DeleteWhere&lt;T&gt;</c>) names no single identity.
/// </param>
internal sealed record MartenDocumentDeletion(Type DocumentType, object? Id): IDocumentDeletion;
