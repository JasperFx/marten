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

    /// <summary>
    ///     Also fetch related documents, and call the callback lambda for each
    ///     related document. Follow this with <c>.On(idSource)</c> to specify how to
    ///     map to this document.
    /// </summary>
    /// <param name="callback"></param>
    /// <typeparam name="TInclude"></typeparam>
    /// <returns></returns>
    IMartenQueryableIncludeBuilder<T, TInclude> Include<TInclude>(Action<TInclude> callback) where TInclude : notnull;

    /// <summary>
    ///     Also fetch related documents, and add the related documents to
    ///     the supplied list. Follow this with <c>.On(idSource)</c> to specify how to
    ///     map to this document.
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="list"></param>
    /// <typeparam name="TInclude"></typeparam>
    /// <returns></returns>
    IMartenQueryableIncludeBuilder<T, TInclude> Include<TInclude>(IList<TInclude> list) where TInclude : notnull;

    /// <summary>
    ///     Also fetch related documents, and add the related documents to
    ///     the supplied dictionary organized by the property mapped to the related
    ///     document. Follow this with <c>.On(idSource)</c> to specify how to map to
    ///     this document.
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="dictionary"></param>
    /// <typeparam name="TInclude"></typeparam>
    /// <typeparam name="TKey"></typeparam>
    /// <returns></returns>
    IMartenQueryableIncludeBuilder<T, TKey, TInclude> Include<TKey, TInclude>(
        IDictionary<TKey, TInclude> dictionary) where TInclude : notnull where TKey : notnull;

    /// <summary>
    ///     Also fetch related documents, and add the related documents to
    ///     the supplied dictionary of lists organized by the property mapped to the
    ///     related document. Follow this with <c>.On(idSource)</c> to specify how
    ///     to map to this document.
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="dictionary"></param>
    /// <typeparam name="TInclude"></typeparam>
    /// <typeparam name="TKey"></typeparam>
    /// <returns></returns>
    IMartenQueryableIncludeBuilder<T, TKey, TInclude> Include<TKey, TInclude>(
        IDictionary<TKey, IList<TInclude>> dictionary) where TInclude : notnull where TKey : notnull;

    /// <summary>
    ///     Also fetch related documents, and add the related documents to
    ///     the supplied dictionary of lists organized by the property mapped to the
    ///     related document. Follow this with <c>.On(idSource)</c> to specify how
    ///     to map to this document.
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="dictionary"></param>
    /// <typeparam name="TInclude"></typeparam>
    /// <typeparam name="TKey"></typeparam>
    /// <returns></returns>
    IMartenQueryableIncludeBuilder<T, TKey, TInclude> Include<TKey, TInclude>(
        IDictionary<TKey, List<TInclude>> dictionary) where TInclude : notnull where TKey : notnull;

    /// <summary>
    ///     Filters the query for documents of a given subclass type within a document hierarchy
    ///     and applies the specified predicate to those subclass documents.
    /// </summary>
    /// <typeparam name="TSub">
    ///     The subclass type to filter on. Must inherit from <typeparamref name="T"/>.
    /// </typeparam>
    /// <param name="predicate">
    ///     A lambda expression representing the filter to apply to documents of type <typeparamref name="TSub"/>.
    /// </param>
    /// <returns>
    ///     An <see cref="IMartenQueryable{T}"/> of the base type <typeparamref name="T"/> with the filter applied
    ///     only to matching subclass documents.
    /// </returns>
    /// <remarks>
    ///     This method is useful for querying on subclass-specific properties while still returning
    ///     results as the base type, preserving the hierarchy in the query.
    /// </remarks>
    IMartenQueryable<T> WhereSub<TSub>(Expression<Func<TSub, bool>> predicate) where TSub : T;
}
