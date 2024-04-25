#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq.Includes;

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
    ///     Also fetch related documents, and call the callback lambda for each
    ///     related document
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="callback"></param>
    /// <param name="filter">Supply a Where() clause to filter the included documents returned</param>
    /// <typeparam name="TInclude"></typeparam>
    /// <returns></returns>
    IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback, Expression<Func<TInclude, bool>> filter)
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
    ///     the supplied list
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="list"></param>
    /// <param name="filter">Specify Where() filtering on the included documents</param>
    /// <typeparam name="TInclude"></typeparam>
    /// <returns></returns>
    IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list, Expression<Func<TInclude, bool>> filter)
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
    ///     Also fetch related documents, and add the related documents to
    ///     the supplied dictionary organized by the identity of the related document
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="dictionary"></param>
    /// <param name="filter">Limit the included documents fetched from the server</param>
    /// <typeparam name="TInclude"></typeparam>
    /// <typeparam name="TKey"></typeparam>
    /// <returns></returns>
    IMartenQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource,
        IDictionary<TKey, TInclude> dictionary, Expression<Func<TInclude, bool>> filter) where TInclude : notnull where TKey : notnull;

    IMartenQueryableIncludeBuilder<T, TInclude> Include<TInclude>(Action<TInclude> callback) where TInclude : notnull;
    IMartenQueryableIncludeBuilder<T, TInclude> Include<TInclude>(IList<TInclude> list) where TInclude : notnull;
    IMartenQueryableIncludeBuilder<T, TId, TInclude> Include<TId, TInclude>(
        IDictionary<TId, TInclude> dictionary) where TInclude : notnull where TId : notnull;
    IMartenQueryableIncludeBuilder<T, TId, TInclude> Include<TId, TInclude>(
        IDictionary<TId, IList<TInclude>> dictionary) where TInclude : notnull where TId : notnull;
}
