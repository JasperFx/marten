using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Internal.Sessions;
using Marten.Linq;

namespace Marten.Events;

public static class AggregateToExtensions
{
    private static void setIdentity<T>(QuerySession session, T aggregate, IEnumerable<IEvent> events) where T : class
    {
        if (session.Options.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            session.StorageFor<T, Guid>().SetIdentity(aggregate, events.Last().StreamId);
        }
        else
        {
            session.StorageFor<T, string>().SetIdentity(aggregate, events.Last().StreamKey);
        }
    }

    /// <summary>
    ///     Aggregate the events in this query to the type T
    /// </summary>
    /// <param name="queryable"></param>
    /// <param name="state"></param>
    /// <param name="token"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static async Task<T> AggregateToAsync<T>(this IMartenQueryable<IEvent> queryable, T state = null,
        CancellationToken token = new()) where T : class
    {
        var events = await queryable.ToListAsync(token).ConfigureAwait(false);
        if (!events.Any())
        {
            return null;
        }

        var session = queryable.As<MartenLinqQueryable<IEvent>>().Session;
        var aggregator = session.Options.Projections.AggregatorFor<T>();

        var aggregate = await aggregator.BuildAsync(events, session, state, token).ConfigureAwait(false);

        setIdentity(session, aggregate, events);

        return aggregate;
    }

    /// <summary>
    ///     Aggregate the events in this query to the type T
    /// </summary>
    /// <param name="queryable"></param>
    /// <param name="state"></param>
    /// <param name="token"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static async Task<T> AggregateToAsync<T>(this IQueryable<IEvent> queryable, T state = null,
        CancellationToken token = new()) where T : class
    {
        return await AggregateToAsync(queryable.As<IMartenQueryable<IEvent>>(), state, token).ConfigureAwait(false);
    }


}
