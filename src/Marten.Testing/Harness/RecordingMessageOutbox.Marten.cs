using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Internal.Sessions;
using Marten.Services;

namespace JasperFx.Events.ComplianceTests;

/*
 * Marten's half of the shared recording message outbox (jasperfx#763, marten#5343).
 *
 * Same division of labour as ComplianceSubscription.Marten.cs beside it: the compliance library
 * owns the recording, the ordering, the commit probe and the locking, and this partial supplies
 * only the interface implementations that genuinely cannot be written once. IMessageOutbox and
 * IMessageBatch are per-product types whose members differ -- Marten declares
 * IMessageBatch : IMessageSink, IChangeListener, so its commit hooks take
 * (IDocumentSession, IChangeSet, CancellationToken), while Polecat's and Fisher's take a bare
 * CancellationToken -- and Marten's CreateBatch takes the internal DocumentSessionBase rather than
 * any shared session type.
 *
 * The IMessageSink half -- PublishAsync<T>(T, string) -- is shared and stays in the library, so
 * nothing here records anything itself; each member is a one-line forward onto the library's
 * protected recorder.
 *
 * Lives beside MartenComplianceFixture rather than in EventSourcingTests for the reason
 * ComplianceSubscription.Marten.cs documents: both assemblies reference the source-only compliance
 * package and therefore both compile the library's half of these partials, so both need this half
 * to satisfy them.
 */
public partial class RecordingMessageOutbox: IMessageOutbox
{
    public ValueTask<IMessageBatch> CreateBatch(DocumentSessionBase session)
        => new(NewBatch());
}

public partial class RecordingMessageBatch: IMessageBatch
{
    public Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        => RecordBeforeCommitAsync();

    public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        => RecordAfterCommitAsync();
}
