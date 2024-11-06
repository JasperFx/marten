using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;

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

public interface IEventDataMasking
{
    /// <summary>
    /// Isolate the event masking to a specific tenant if using multi-tenancy
    /// </summary>
    /// <param name="tenantId"></param>
    /// <returns></returns>
    IEventDataMasking ForTenant(string tenantId);

    /// <summary>
    /// Apply data protection masking to this event stream
    /// </summary>
    /// <param name="streamId"></param>
    /// <returns></returns>
    IEventDataMasking IncludeStream(Guid streamId);

    /// <summary>
    /// Apply data protection masking to this event stream
    /// </summary>
    /// <param name="streamKey"></param>
    /// <returns></returns>
    IEventDataMasking IncludeStream(string streamKey);

    /// <summary>
    /// Apply data protection masking to this event stream
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="filter">Further filter events within the stream to more finely target events for masking</param>
    /// <returns></returns>
    IEventDataMasking IncludeStream(Guid streamId, Func<IEvent, bool> filter);

    /// <summary>
    /// Apply data protection masking to this event stream
    /// </summary>
    /// <param name="streamKey"></param>
    /// <param name="filter">Further filter events within the stream to more finely target events for masking</param>
    /// <returns></returns>
    IEventDataMasking IncludeStream(string streamKey, Func<IEvent, bool> filter);

    /// <summary>
    /// Apply data protection masking to events matching
    /// this criteria
    /// </summary>
    /// <param name="filter"></param>
    /// <returns></returns>
    IEventDataMasking IncludeEvents(Expression<Func<IEvent, bool>> filter);

    /// <summary>
    /// Add a new header value to the metadata for any event that is masked
    /// as part of this batch operation. Note that this will only apply to
    /// event types that have a matching masking rule
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    IEventDataMasking AddHeader(string key, object value);
}
