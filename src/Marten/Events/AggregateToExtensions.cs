using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Internal.Sessions;
using Marten.Linq;

using Marten.Internal;

namespace Marten.Events;

public static class AggregateToExtensions
{
    private static readonly MethodInfo AggregateManyMethod = typeof(AggregateToExtensions)
        .GetMethod(nameof(aggregateManyAsync), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void setIdentity<T>(QuerySession session, T aggregate, IEnumerable<IEvent> events) where T : class
    {
        if (((IMartenSession)session).Options.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            session.StorageFor<T, Guid>().SetIdentity(aggregate, events.Last().StreamId);
        }
        else
        {
            session.StorageFor<T, string>().SetIdentity(aggregate, events.Last().StreamKey!);
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
    public static async Task<T?> AggregateToAsync<T>(this IMartenQueryable<IEvent> queryable, T? state = null,
        CancellationToken token = new()) where T : class
    {
        var events = await queryable.ToListAsync(token).ConfigureAwait(false);
        if (!events.Any())
        {
            return null;
        }

        var session = queryable.As<MartenLinqQueryable<IEvent>>().Session;
        var aggregator = ((IMartenSession)session).Options.Projections.AggregatorFor<T>();

        var aggregate = await aggregator.BuildAsync(events, session, state, token).ConfigureAwait(false)!;

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
    public static async Task<T?> AggregateToAsync<T>(this IQueryable<IEvent> queryable, T? state = null,
        CancellationToken token = new()) where T : class
    {
        return await AggregateToAsync(queryable.As<IMartenQueryable<IEvent>>(), state, token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Run the events matched by this query through the multi-stream projection registered for
    ///     <typeparamref name="T"/> and return the aggregate it produces for each resulting identity. This is
    ///     the live-query twin of the projection step-through's multi-stream run: it drives the projection's
    ///     REAL slicer/grouper and per-slice build (the same core the step-through uses, minus the step
    ///     observer) against the live query session, so enrichment that reads reference data works for free.
    ///     Contrast <see cref="AggregateToAsync{T}(IMartenQueryable{IEvent},T,CancellationToken)"/>, which folds
    ///     every queried event into a single aggregate.
    /// </summary>
    public static async Task<IReadOnlyList<T>> AggregateToManyAsync<T>(this IMartenQueryable<IEvent> queryable,
        CancellationToken token = new()) where T : class
    {
        var session = queryable.As<MartenLinqQueryable<IEvent>>().Session;
        var options = ((IMartenSession)session).Options;

        // Validate the projection up front (even for an empty result set) so a call for an aggregate type
        // that has no registered projection is a clear programming error rather than a silent empty list.
        var projection = options.Projections.All
                             .OfType<IAggregateProjection>()
                             .FirstOrDefault(x => x.AggregateType == typeof(T))
                         ?? throw new ArgumentException(
                             $"No aggregate projection is registered that produces '{typeof(T).FullNameInCode()}'. AggregateToMany() runs an event query through a registered (multi-stream) projection.",
                             nameof(queryable));

        var idType = findAggregateIdType(projection.GetType())
                     ?? throw new ArgumentException(
                         $"Projection '{projection.GetType().FullNameInCode()}' for '{typeof(T).FullNameInCode()}' is not a slicing aggregate projection that AggregateToMany() can drive.",
                         nameof(queryable));

        var events = await queryable.ToListAsync(token).ConfigureAwait(false);
        if (events.Count == 0)
        {
            return Array.Empty<T>();
        }

        var closed = AggregateManyMethod.MakeGenericMethod(typeof(T), idType);
        var task = (Task<IReadOnlyList<T>>)closed.Invoke(null, [projection, events, session, token])!;
        return await task.ConfigureAwait(false);
    }

    /// <summary>
    ///     Run the events matched by this query through the multi-stream projection registered for
    ///     <typeparamref name="T"/> and return one aggregate per resulting identity.
    /// </summary>
    public static Task<IReadOnlyList<T>> AggregateToManyAsync<T>(this IQueryable<IEvent> queryable,
        CancellationToken token = new()) where T : class
    {
        return queryable.As<IMartenQueryable<IEvent>>().AggregateToManyAsync<T>(token);
    }

    // TId is not statically known at the AggregateToManyAsync<T> call site, so drive the strongly-typed
    // slice -> enrich -> build fold through a TId-closed helper invoked via reflection.
    private static async Task<IReadOnlyList<T>> aggregateManyAsync<T, TId>(
        JasperFxAggregationProjectionBase<T, TId, IDocumentOperations, IQuerySession> projection,
        IReadOnlyList<IEvent> events,
        QuerySession session,
        CancellationToken token)
        where T : class where TId : notnull
    {
        // The SAME building blocks the step-through fold uses (BuildTimelinesAsync): the projection's own
        // slicer/grouper, then EnrichEventsAsync per group, then DetermineActionAsync (Create/Apply/
        // ShouldDelete dispatch) per slice — so the live-query result can't diverge from the step-through.
        var slicer = projection.BuildSlicer(session);
        var groups = await slicer.SliceAsync(events).ConfigureAwait(false);

        var identitySetter = new NulloIdentitySetter<T, TId>();
        var storage = session.StorageFor<T, TId>();
        var results = new List<T>();

        foreach (var groupObject in groups)
        {
            if (groupObject is not SliceGroup<T, TId> group)
            {
                continue;
            }

            await projection.EnrichEventsAsync(group, session, token).ConfigureAwait(false);

            foreach (var slice in group.Slices)
            {
                var (aggregate, action) = await projection
                    .DetermineActionAsync(session, default, slice.Id, identitySetter, slice.Events(), token)
                    .ConfigureAwait(false);

                if (action == ActionType.Delete || aggregate is null)
                {
                    continue;
                }

                // The fold uses a Nullo identity setter, so stamp the aggregate's own id from the slice.
                storage.SetIdentity(aggregate, slice.Id);
                results.Add(aggregate);
            }
        }

        return results;
    }

    // Walk the projection's base chain for the closed JasperFxAggregationProjectionBase<TDoc, TId, ...>
    // and return its TId argument.
    private static Type? findAggregateIdType(Type projectionType)
    {
        var type = projectionType;
        while (type != null && type != typeof(object))
        {
            if (type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(JasperFxAggregationProjectionBase<,,,>))
            {
                return type.GetGenericArguments()[1];
            }

            type = type.BaseType;
        }

        return null;
    }
}
