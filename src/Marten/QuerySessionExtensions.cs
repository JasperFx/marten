using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Marten
{
    public static class QuerySessionExtensions
    {
        private static readonly MethodInfo QueryMethod = typeof(IQuerySession).GetMethod(nameof(IQuerySession.Query), new[] { typeof(string), typeof(object[]) });
        private static readonly MethodInfo QueryMethodAsync = typeof(IQuerySession).GetMethod(nameof(IQuerySession.QueryAsync), new[] { typeof(string), typeof(CancellationToken), typeof(object[]) });

        /// <summary>
        /// Query by a user-supplied .Net document type and user-supplied SQL
        /// </summary>
        /// <param name="session"></param>
        /// <param name="type"></param>
        /// <param name="sql"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static IReadOnlyList<object> Query(this IQuerySession session, Type type, string sql, params object[] parameters)
        {
            return (IReadOnlyList<object>)QueryMethod.MakeGenericMethod(type).Invoke(session, new object[] { sql, parameters });
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
            var task = (Task)QueryMethodAsync.MakeGenericMethod(type).Invoke(session, new object[] { sql, token, parameters });
            await task;
            return (IReadOnlyList<object>)task.GetType().GetProperty("Result").GetValue(task);
        }
    }
}
