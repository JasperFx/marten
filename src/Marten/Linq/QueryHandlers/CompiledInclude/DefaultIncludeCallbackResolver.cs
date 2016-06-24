using System;
using System.Collections.Generic;
using System.Reflection;
using Baseline;

namespace Marten.Linq.QueryHandlers.CompiledInclude
{
    public class DefaultIncludeCallbackResolver<TInclude, TDoc, TOut> : IncludeCallbackResolver, IIncludeCallbackResolver<TInclude>
    {
        private readonly ICompiledQuery<TDoc, TOut> _query;
        private readonly IncludeResultOperator _includeOperator;

        public DefaultIncludeCallbackResolver(ICompiledQuery<TDoc, TOut> query, IncludeResultOperator includeOperator)
        {
            _query = query;
            _includeOperator = includeOperator;
        }

        public Action<TInclude> Resolve(PropertyInfo property, IncludeTypeContainer typeContainer)
        {
            Action<TInclude> callback;

            if (typeContainer.IncludeType.IsGenericEnumerable())
            {
                typeContainer.IncludeType = typeContainer.IncludeType.GenericTypeArguments[0];
                callback = GetJoinListCallback(property, _includeOperator, _query);
            }
            else
            {
                callback = GetJoinCallback(property, _includeOperator, _query);
            }
            return callback;
        }

        private static Action<TInclude> GetJoinCallback(PropertyInfo property, IncludeResultOperator @operator, ICompiledQuery<TDoc, TOut> query)
        {
            var queryProperty = GetPropertyInfo(property, @operator);

            return x => queryProperty.SetValue(query, x);
        }

        private static Action<TInclude> GetJoinListCallback(PropertyInfo property, IncludeResultOperator @operator, ICompiledQuery<TDoc, TOut> query)
        {
            var queryProperty = GetPropertyInfo(property, @operator);

            var included = (IList<TInclude>)(queryProperty).GetValue(query);
            if (included == null)
            {
                queryProperty.SetValue(query, new List<TInclude>());
                included = (IList<TInclude>)queryProperty.GetValue(query);
            }

            return included.Fill;
        }
    }
}