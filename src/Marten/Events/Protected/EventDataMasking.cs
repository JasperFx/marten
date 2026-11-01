using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Protected;
using Marten.Events.Archiving;
using Marten.Internal;

namespace Marten.Events.Protected;

public class EventDataMasking : IEventDataMasking
{
    private readonly DocumentStore _store;
    private readonly List<Func<IDocumentSession, CancellationToken, Task<IReadOnlyList<IEvent>>>> _sources = new();
    private readonly Dictionary<string, object> _headers = new();
    private string _tenantId;

    public EventDataMasking(DocumentStore store)
    {
        _store = store;
    }

    public IEventDataMasking ForTenant(string tenantId)
    {
        _tenantId = tenantId;
        return this;
    }

    public IEventDataMasking IncludeStream(Guid streamId)
    {
        _sources.Add((s, t) => fetchStreamAsync(s, streamId, t));
        return this;
    }

    public IEventDataMasking IncludeStream(string streamKey)
    {
        _sources.Add((s, t) => fetchStreamAsync(s, streamKey, t));
        return this;
    }

    public IEventDataMasking IncludeStream(Guid streamId, Func<IEvent, bool> filter)
    {
        _sources.Add(async (s, t) =>
        {
            var raw = await fetchStreamAsync(s, streamId, t).ConfigureAwait(false);
            return raw.Where(filter).ToList();
        });

        return this;
    }

    public IEventDataMasking IncludeStream(string streamKey, Func<IEvent, bool> filter)
    {
        _sources.Add(async (s, t) =>
        {
            var raw = await fetchStreamAsync(s, streamKey, t).ConfigureAwait(false);
            return raw.Where(filter).ToList();
        });

        return this;
    }

    public IEventDataMasking IncludeEvents(Expression<Func<IEvent, bool>> filter)
    {
        // A filter that names IsArchived itself still narrows this, so it can still rule them back out
        _sources.Add((s, t) => s.Events.QueryAllRawEvents().Where(x => x.MaybeArchived()).Where(filter).ToListAsync(t));
        return this;
    }

    public IEventDataMasking AddHeader(string key, object value)
    {
        _headers[key] = value;
        return this;
    }

    public async Task ApplyAsync(CancellationToken token = default)
    {
        if (!_sources.Any())
            throw new InvalidOperationException(
                "You need to specify at least one stream identity or event filter first as part of the Fluent Interface");

        var session = BuildSession();

        foreach (var source in _sources)
        {
            var events = await source(session, token).ConfigureAwait(false);
            foreach (var @event in events)
            {
                if (_store.Events.TryMask(@event))
                {
                    foreach (var pair in _headers)
                    {
                        @event.Headers ??= new();
                        @event.Headers[pair.Key] = pair.Value;
                    }

                    session.Events.OverwriteEvent(@event);
                }
            }
        }

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    // Archiving a stream takes it out of the normal reads and leaves it on disk, so masking has to reach it.
    // FetchStreamAsync leaves archived rows out, and skipped ones when skipping is on, which found nothing,
    // erased nothing and reported success. This read loses the identity check FetchStreamAsync makes, and a
    // Guid against a string store would come back as the same silent nothing, so that check is made here.
    private Task<IReadOnlyList<IEvent>> fetchStreamAsync(IDocumentSession session, Guid streamId, CancellationToken token)
    {
        _store.Events.EnsureAsGuidStorage((IMartenSession)session);

        return session.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamId == streamId)
            .OrderBy(x => x.Version)
            .ToListAsync(token);
    }

    private Task<IReadOnlyList<IEvent>> fetchStreamAsync(IDocumentSession session, string streamKey, CancellationToken token)
    {
        _store.Events.EnsureAsStringStorage((IMartenSession)session);

        return session.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamKey == streamKey)
            .OrderBy(x => x.Version)
            .ToListAsync(token);
    }

    internal IDocumentSession BuildSession()
    {
        var session = _tenantId.IsEmpty() ? _store.LightweightSession() : _store.LightweightSession(_tenantId);
        return session;
    }
}

// IEventDataMasking used to be declared here. jasperfx#635 / marten#5154 lifted it into
// JasperFx.Events.Protected, beside StreamCompactingRequest<T>: the two products declared it
// member-for-member identically, and the fluent shape is a database-agnostic description of intent
// even though executing it is unavoidably store-specific. EventDataMasking above still implements
// it and is unchanged; only the declaration moved.
