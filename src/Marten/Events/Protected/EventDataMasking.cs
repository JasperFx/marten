using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Protected;
using Marten.Events.Archiving;
using Marten.Internal;

namespace Marten.Events.Protected;

/// <summary>
///     Marten's <see cref="IEventDataMasking" />. The fluent surface and the pass itself are the shared
///     <see cref="EventDataMaskingBase{TSession}" /> (jasperfx#1048); this supplies Marten's session and its
///     reads, which reach archived events because archiving a stream does not erase it.
/// </summary>
public class EventDataMasking : EventDataMaskingBase<IDocumentSession>
{
    private readonly DocumentStore _store;

    public EventDataMasking(DocumentStore store)
    {
        _store = store;
    }

    protected override IDocumentSession OpenSession(string? tenantId)
    {
        return tenantId.IsEmpty() ? _store.LightweightSession() : _store.LightweightSession(tenantId!);
    }

    protected override IEventStoreOperations EventsFor(IDocumentSession session) => session.Events;

    // Archiving a stream takes it out of the normal reads and leaves it on disk, so masking has to reach it.
    // FetchStreamAsync leaves archived rows out, and skipped ones when skipping is on, which found nothing,
    // erased nothing and reported success. This read loses the identity check FetchStreamAsync makes, and a
    // Guid against a string store would come back as the same silent nothing, so that check is made here.
    protected override Task<IReadOnlyList<IEvent>> FetchStreamIncludingArchivedAsync(IDocumentSession session,
        Guid streamId, CancellationToken token)
    {
        _store.Events.EnsureAsGuidStorage((IMartenSession)session);

        return session.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamId == streamId)
            .OrderBy(x => x.Version)
            .ToListAsync(token);
    }

    protected override Task<IReadOnlyList<IEvent>> FetchStreamIncludingArchivedAsync(IDocumentSession session,
        string streamKey, CancellationToken token)
    {
        _store.Events.EnsureAsStringStorage((IMartenSession)session);

        return session.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamKey == streamKey)
            .OrderBy(x => x.Version)
            .ToListAsync(token);
    }

    // A filter that names IsArchived itself still narrows this, so it can still rule them back out
    protected override Task<IReadOnlyList<IEvent>> QueryEventsIncludingArchivedAsync(IDocumentSession session,
        Expression<Func<IEvent, bool>> filter, CancellationToken token)
    {
        return session.Events.QueryAllRawEvents().Where(x => x.MaybeArchived()).Where(filter).ToListAsync(token);
    }

    protected override bool TryMask(IEvent @event) => _store.Events.TryMask(@event);

    protected override Task SaveChangesAsync(IDocumentSession session, CancellationToken token)
        => session.SaveChangesAsync(token);
}

// IEventDataMasking used to be declared here. jasperfx#635 / marten#5154 lifted it into
// JasperFx.Events.Protected, beside StreamCompactingRequest<T>: the two products declared it
// member-for-member identically, and the fluent shape is a database-agnostic description of intent
// even though executing it is unavoidably store-specific. EventDataMasking above still implements
// it and is unchanged; only the declaration moved.
