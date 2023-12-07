#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;

namespace Marten.Linq;


public interface IMartenQueryable<T>: IQueryable<T>
{
    /// <summary>
    ///     Also fetch related documents, and call the callback lambda for each
    ///     related document
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="callback"></param>
    /// <typeparam name="TInclude"></typeparam>
    /// <returns></returns>
    IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback)
        where TInclude : notnull;

    /// <summary>
    ///     Also fetch related documents, and add the related documents to
    ///     the supplied list
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="list"></param>
    /// <typeparam name="TInclude"></typeparam>
    /// <returns></returns>
    IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list)
        where TInclude : notnull;

    /// <summary>
    ///     Also fetch related documents, and add the related documents to
    ///     the supplied dictionary organized by the identity of the related document
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="dictionary"></param>
    /// <typeparam name="TInclude"></typeparam>
    /// <typeparam name="TKey"></typeparam>
    /// <returns></returns>
    IMartenQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource,
        IDictionary<TKey, TInclude> dictionary) where TInclude : notnull where TKey : notnull;

    /// <summary>
    ///     Retrieve the total number of persisted rows in the database that match this
    ///     query. Useful for server side paging.
    /// </summary>
    /// <param name="stats"></param>
    /// <returns></returns>
    IMartenQueryable<T> Stats(out QueryStatistics stats);


}
