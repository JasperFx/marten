using System;
using System.Collections.Generic;
using System.Reflection;
using Baseline;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Linq.QueryHandlers.CompiledInclude
{
    public class DictionaryIncludeCallbackResolver<TKey, TInclude, TDoc, TOut> : IncludeCallbackResolver, IIncludeCallbackResolver<TInclude>
    {
        private readonly ICompiledQuery<TDoc, TOut> _query;
        private readonly IncludeResultOperator _includeOperator;
        private readonly StorageFeatures _storage;

        public DictionaryIncludeCallbackResolver(ICompiledQuery<TDoc, TOut> query, IncludeResultOperator includeOperator, StorageFeatures storage)
        {
            _query = query;
            _includeOperator = includeOperator;
            _storage = storage;
        }

        public Action<TInclude> Resolve(PropertyInfo property, IncludeTypeContainer typeContainer)
        {
            typeContainer.IncludeType = typeContainer.IncludeType.GenericTypeArguments[1];
            return GetJoinDictionaryCallback(property, _includeOperator, _query);
        }

        private Action<TInclude> GetJoinDictionaryCallback(PropertyInfo property, IncludeResultOperator @operator, ICompiledQuery<TDoc, TOut> query)
        {
            var queryProperty = GetPropertyInfo(property, @operator);

            var storage = _storage.StorageFor(typeof(TInclude));

            var dictionary = (IDictionary<TKey, TInclude>)(queryProperty).GetValue(query);
            if (dictionary == null)
            {
                queryProperty.SetValue(query, new Dictionary<TKey, TInclude>());
                dictionary = (IDictionary<TKey, TInclude>)queryProperty.GetValue(query);
            }

            return x => {
                var id = storage.Identity(x).As<TKey>();
                if (!dictionary.ContainsKey(id))
                {
                    dictionary.Add(id, x);
                }
            };
        }
    }
}