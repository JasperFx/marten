using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Linq;
using Marten.Services.BatchQuerying;

namespace Marten;

/// <summary>
/// Marten's concept of the "Specification" pattern for reusable
/// queries. Use this for operations that cannot be supported by Marten compiled queries
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IQueryPlan<T>
{
    Task<T> Fetch(IQuerySession session, CancellationToken token);
}

#region sample_ibatchqueryplan

/// <summary>
/// Marten's concept of the "Specification" pattern for reusable
/// queries within Marten batched queries. Use this for operations that cannot be supported by Marten compiled queries
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IBatchQueryPlan<T>
{
    Task<T> Fetch(IBatchedQuery query);
}

#endregion

/// <summary>
/// Base class for query plans for a list of items. Implementations of this abstract type
/// can be used both individually with IQuerySession.QueryByPlan() and with IBatchedQuery.QueryByPlan()
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class QueryListPlan<T> : IQueryPlan<IReadOnlyList<T>>, IBatchQueryPlan<IReadOnlyList<T>> where T : notnull
{
    /// <summary>
    /// Return an IQueryable<T> from the IQuerySession to define the query plan
    /// for Marten
    /// </summary>
    /// <param name="session"></param>
    /// <returns></returns>
    public abstract IQueryable<T> Query(IQuerySession session);


    Task<IReadOnlyList<T>> IQueryPlan<IReadOnlyList<T>>.Fetch(IQuerySession session, CancellationToken token)
    {
        return Query(session).ToListAsync(token);
    }


    Task<IReadOnlyList<T>> IBatchQueryPlan<IReadOnlyList<T>>.Fetch(IBatchedQuery query)
    {
        var queryable = Query(query.Parent) as MartenLinqQueryable<T>;
        if (queryable == null)
            throw new InvalidOperationException("Marten is not able to use this QueryListPlan in batch querying");

        var handler = queryable.BuilderListHandler();

        return query.AddItem(handler);
    }
}

/// <summary>
/// Query plan to fetch the high level metadata about a single event stream identified by
/// either a Guid stream id or a string stream key. Can be used both individually with
/// IQuerySession.QueryByPlanAsync() and with IBatchedQuery.QueryByPlan(). Yields null
/// if the stream does not exist
/// </summary>
public class FetchStreamStatePlan : IQueryPlan<StreamState?>, IBatchQueryPlan<StreamState?>
{
    private readonly Guid _streamId;
    private readonly string? _streamKey;

    /// <summary>
    /// Fetch the stream state for the stream identified by <paramref name="streamId"/>
    /// </summary>
    /// <param name="streamId"></param>
    public FetchStreamStatePlan(Guid streamId)
    {
        _streamId = streamId;
    }

    /// <summary>
    /// Fetch the stream state for the stream identified by <paramref name="streamKey"/>
    /// </summary>
    /// <param name="streamKey"></param>
    public FetchStreamStatePlan(string streamKey)
    {
        _streamKey = streamKey;
    }

    public Task<StreamState?> Fetch(IQuerySession session, CancellationToken token)
    {
        return _streamKey is not null
            ? session.Events.FetchStreamStateAsync(_streamKey, token)
            : session.Events.FetchStreamStateAsync(_streamId, token);
    }

    public async Task<StreamState?> Fetch(IBatchedQuery query)
    {
        return _streamKey is not null
            ? await query.Events.FetchStreamState(_streamKey).ConfigureAwait(false)
            : await query.Events.FetchStreamState(_streamId).ConfigureAwait(false);
    }
}

/// <summary>
/// Query plan to fetch the raw events for a single event stream identified by either a
/// Guid stream id or a string stream key. Can be used both individually with
/// IQuerySession.QueryByPlanAsync() and with IBatchedQuery.QueryByPlan(). Yields an
/// empty list if the stream does not exist
/// </summary>
public class FetchStreamPlan : IQueryPlan<IReadOnlyList<IEvent>>, IBatchQueryPlan<IReadOnlyList<IEvent>>
{
    private readonly Guid _streamId;
    private readonly string? _streamKey;
    private readonly long _version;
    private readonly DateTimeOffset? _timestamp;
    private readonly long _fromVersion;

    /// <summary>
    /// Fetch the events for the stream identified by <paramref name="streamId"/>
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="version">If set, queries for events up to and including this version</param>
    /// <param name="timestamp">If set, queries for events captured on or before this timestamp</param>
    /// <param name="fromVersion">If set, queries for events on or from this version</param>
    public FetchStreamPlan(Guid streamId, long version = 0, DateTimeOffset? timestamp = null, long fromVersion = 0)
    {
        _streamId = streamId;
        _version = version;
        _timestamp = timestamp;
        _fromVersion = fromVersion;
    }

    /// <summary>
    /// Fetch the events for the stream identified by <paramref name="streamKey"/>
    /// </summary>
    /// <param name="streamKey"></param>
    /// <param name="version">If set, queries for events up to and including this version</param>
    /// <param name="timestamp">If set, queries for events captured on or before this timestamp</param>
    /// <param name="fromVersion">If set, queries for events on or from this version</param>
    public FetchStreamPlan(string streamKey, long version = 0, DateTimeOffset? timestamp = null, long fromVersion = 0)
    {
        _streamKey = streamKey;
        _version = version;
        _timestamp = timestamp;
        _fromVersion = fromVersion;
    }

    public Task<IReadOnlyList<IEvent>> Fetch(IQuerySession session, CancellationToken token)
    {
        return _streamKey is not null
            ? session.Events.FetchStreamAsync(_streamKey, _version, _timestamp, _fromVersion, token)
            : session.Events.FetchStreamAsync(_streamId, _version, _timestamp, _fromVersion, token);
    }

    public Task<IReadOnlyList<IEvent>> Fetch(IBatchedQuery query)
    {
        return _streamKey is not null
            ? query.Events.FetchStream(_streamKey, _version, _timestamp, _fromVersion)
            : query.Events.FetchStream(_streamId, _version, _timestamp, _fromVersion);
    }
}
