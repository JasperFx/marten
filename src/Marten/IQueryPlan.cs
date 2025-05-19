using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

#region sample_IBatchQueryPlan

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
