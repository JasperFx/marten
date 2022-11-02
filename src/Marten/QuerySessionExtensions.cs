using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline.ImTools;

namespace Marten
{
    public static class QuerySessionExtensions
    {
        private static readonly MethodInfo QueryMethod = typeof(IQuerySession).GetMethod(nameof(IQuerySession.Query), new[] { typeof(string), typeof(object[]) });
        private static readonly MethodInfo QueryMethodAsync = typeof(IQuerySession).GetMethod(nameof(IQuerySession.QueryAsync), new[] { typeof(string), typeof(CancellationToken), typeof(object[]) });

        private static Ref<ImHashMap<Type, MethodInfo>> QueryMethods =
            Ref.Of(ImHashMap<Type, MethodInfo>.Empty);
        private static Ref<ImHashMap<Type, MethodInfo>> QueryAsyncMethods =
            Ref.Of(ImHashMap<Type, MethodInfo>.Empty);

        /// <summary>
        /// Query by a user-supplied .Net document type and user-supplied SQL
        /// </summary>
        /// <param name="session"></param>
        /// <param name="type"></param>
        /// <param name="sql"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static IReadOnlyList<object> Query(this IQuerySession session, Type type, string sql, params object[] parameters) =>
            (IReadOnlyList<object>)QueryFor(type).Invoke(session, new object[] { sql, parameters });

        private static MethodInfo QueryFor(Type type)
        {
            if (QueryMethods.Value.TryFind(type, out var method))
                return method;

            method = QueryMethod.MakeGenericMethod(type);
            QueryMethods.Swap(d => d.AddOrUpdate(type, method));

            return method;
        }

        /// <summary>
        /// Query by a user-supplied .Net document type and user-supplied SQL
        /// </summary>
        /// <param name="session"></param>
        /// <param name="type"></param>
        /// <param name="sql"></param>
        /// <param name="token"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static async Task<IReadOnlyList<object>> QueryAsync(this IQuerySession session, Type type, string sql, CancellationToken token = default, params object[] parameters)
        {
            var task = (Task)QueryAsyncFor(type).Invoke(session, new object[] { sql, token, parameters });
            await task.ConfigureAwait(false);
            return (IReadOnlyList<object>)((dynamic)task).Result;
        }

        private static MethodInfo QueryAsyncFor(Type type)
        {
            if (QueryAsyncMethods.Value.TryFind(type, out var method))
                return method;

            method = QueryMethodAsync.MakeGenericMethod(type);
            QueryAsyncMethods.Swap(d => d.AddOrUpdate(type, method));

            return method;
        }
    }
}
