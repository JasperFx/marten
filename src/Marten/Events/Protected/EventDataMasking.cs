using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Protected;

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
        _sources.Add((s, t) => s.Events.FetchStreamAsync(streamId, token: t));
        return this;
    }

    public IEventDataMasking IncludeStream(string streamKey)
    {
        _sources.Add((s, t) => s.Events.FetchStreamAsync(streamKey, token: t));
        return this;
    }

    public IEventDataMasking IncludeStream(Guid streamId, Func<IEvent, bool> filter)
    {
        _sources.Add(async (s, t) =>
        {
            var raw = await s.Events.FetchStreamAsync(streamId, token: t).ConfigureAwait(false);
            return raw.Where(filter).ToList();
        });

        return this;
    }

    public IEventDataMasking IncludeStream(string streamKey, Func<IEvent, bool> filter)
    {
        _sources.Add(async (s, t) =>
        {
            var raw = await s.Events.FetchStreamAsync(streamKey, token: t).ConfigureAwait(false);
            return raw.Where(filter).ToList();
        });

        return this;
    }

    public IEventDataMasking IncludeEvents(Expression<Func<IEvent, bool>> filter)
    {
        _sources.Add((s, t) => s.Events.QueryAllRawEvents().Where(filter).ToListAsync(t));
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
