using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx.Core;
using JasperFx.Core.Reflection;

namespace Marten;

public static class QuerySessionExtensions
{
    private static ImHashMap<Type, ITypeQueryExecutor> _queryExecutors = ImHashMap<Type, ITypeQueryExecutor>.Empty;

    private interface ITypeQueryExecutor
    {
        Task<IReadOnlyList<object>> QueryAsync(IQuerySession session, CancellationToken token, string sql,
            params object[] parameters);
    }

    private class TypeQueryExecutor<T>: ITypeQueryExecutor where T : class
    {
        public async Task<IReadOnlyList<object>> QueryAsync(IQuerySession session, CancellationToken token, string sql,
            params object[] parameters)
        {
            var data = await session.QueryAsync<T>(sql, token, parameters).ConfigureAwait(false);
            return data;
        }
    }

    /// <summary>
    ///     Query by a user-supplied .Net document type and user-supplied SQL
    /// </summary>
    /// <param name="session"></param>
    /// <param name="type"></param>
    /// <param name="sql"></param>
    /// <param name="token"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    public static Task<IReadOnlyList<object>> QueryAsync(this IQuerySession session, Type type, string sql,
        CancellationToken token, params object[] parameters)
    {
        if (!_queryExecutors.TryFind(type, out var executor))
        {
            executor = typeof(TypeQueryExecutor<>).CloseAndBuildAs<ITypeQueryExecutor>(type);
            _queryExecutors = _queryExecutors.AddOrUpdate(type, executor);
        }

        return executor.QueryAsync(session, token, sql, parameters);
    }

    /// <summary>
    ///     Query by a user-supplied .Net document type and user-supplied SQL
    /// </summary>
    /// <param name="session"></param>
    /// <param name="type"></param>
    /// <param name="sql"></param>
    /// <param name="token"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    public static Task<IReadOnlyList<object>> QueryAsync(this IQuerySession session, Type type, string sql,
        params object[] parameters)
    {
        if (!_queryExecutors.TryFind(type, out var executor))
        {
            executor = typeof(TypeQueryExecutor<>).CloseAndBuildAs<ITypeQueryExecutor>(type);
            _queryExecutors = _queryExecutors.AddOrUpdate(type, executor);
        }

        return executor.QueryAsync(session, CancellationToken.None, sql, parameters);
    }

}
