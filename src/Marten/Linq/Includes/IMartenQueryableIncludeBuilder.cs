#nullable enable
using System;
using System.Linq.Expressions;

namespace Marten.Linq.Includes;

public interface IMartenQueryableIncludeBuilder<T, TInclude> where TInclude : notnull
{
    /// <summary>
    /// Specify which property to use to map to the identity of the related document.
    /// </summary>
    /// <param name="idSource"></param>
    /// <returns></returns>
    IMartenQueryable<T> On(Expression<Func<T, object>> idSource);

    /// <summary>
    /// Specify which property to use to map to the identity of the related document.
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="filter">Limit the included documents fetched from the server</param>
    /// <returns></returns>
    IMartenQueryable<T> On(Expression<Func<T, object>> idSource, Expression<Func<TInclude, bool>> filter);

    /// <summary>
    /// Specify which property on the queried document to map with, and the property
    /// on the related document to map to.
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="idMapping"></param>
    /// <returns></returns>
    IMartenQueryable<T> On<TId>(Expression<Func<T, TId?>> idSource, Expression<Func<TInclude, TId?>> idMapping);

    /// <summary>
    /// Specify which property on the queried document to map with, and the property
    /// on the related document to map to.
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="idMapping"></param>
    /// <param name="filter">Limit the included documents fetched from the server</param>
    /// <returns></returns>
    IMartenQueryable<T> On<TId>(
        Expression<Func<T, TId?>> idSource,
        Expression<Func<TInclude, TId?>> idMapping,
        Expression<Func<TInclude, bool>> filter);
}

public interface IMartenQueryableIncludeBuilder<T, TKey, TInclude> where TInclude : notnull where TKey : notnull
{
    /// <summary>
    /// Specify which property to use to map to the identity of the related document.
    /// </summary>
    /// <param name="idSource"></param>
    /// <returns></returns>
    IMartenQueryable<T> On(Expression<Func<T, object>> idSource);

    /// <summary>
    /// Specify which property to use to map to the identity of the related document.
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="filter">Limit the included documents fetched from the server</param>
    /// <returns></returns>
    IMartenQueryable<T> On(Expression<Func<T, object>> idSource, Expression<Func<TInclude, bool>> filter);

    /// <summary>
    /// Specify which property on the queried document to map with, and the property
    /// on the related document to map to.
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="idMapping"></param>
    /// <returns></returns>
    IMartenQueryable<T> On(Expression<Func<T, TKey?>> idSource, Expression<Func<TInclude, TKey?>> idMapping);

    /// <summary>
    /// Specify which property on the queried document to map with, and the property
    /// on the related document to map to.
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="idMapping"></param>
    /// <param name="filter">Limit the included documents fetched from the server</param>
    /// <returns></returns>
    IMartenQueryable<T> On(
        Expression<Func<T, TKey?>> idSource,
        Expression<Func<TInclude, TKey?>> idMapping,
        Expression<Func<TInclude, bool>> filter);

    /// <summary>
    /// Specify which property on the queried document to map with, and the property
    /// on the related document to map to.
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="idMapping"></param>
    /// <typeparam name="TId">The key type, as a value-type</typeparam>
    /// <returns></returns>
    IMartenQueryable<T> On<TId>(Expression<Func<T, TId?>> idSource, Expression<Func<TInclude, TId?>> idMapping)
        where TId : struct, TKey;

    /// <summary>
    /// Specify which property on the queried document to map with, and the property
    /// on the related document to map to.
    /// </summary>
    /// <param name="idSource"></param>
    /// <param name="idMapping"></param>
    /// <param name="filter">Limit the included documents fetched from the server</param>
    /// <typeparam name="TId">The key type, as a value-type</typeparam>
    /// <returns></returns>
    IMartenQueryable<T> On<TId>(
        Expression<Func<T, TId?>> idSource,
        Expression<Func<TInclude, TId?>> idMapping,
        Expression<Func<TInclude, bool>> filter)
        where TId : struct, TKey;
}
