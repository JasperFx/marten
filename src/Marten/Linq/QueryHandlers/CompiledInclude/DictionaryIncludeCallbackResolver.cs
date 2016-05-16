using System;
using System.Collections.Generic;
using System.Reflection;
using Baseline;
using Marten.Schema;

namespace Marten.Linq.QueryHandlers.CompiledInclude
{
    public class DictionaryIncludeCallbackResolver<TKey, TInclude, TDoc, TOut> : IncludeCallbackResolver, IIncludeCallbackResolver<TInclude>
    {
        private readonly ICompiledQuery<TDoc, TOut> _query;
        private readonly IncludeResultOperator _includeOperator;
        private readonly IDocumentSchema _schema;

        public DictionaryIncludeCallbackResolver(ICompiledQuery<TDoc, TOut> query, IncludeResultOperator includeOperator, IDocumentSchema schema)
        {
            _query = query;
            _includeOperator = includeOperator;
            _schema = schema;
        }

        public Action<TInclude> Resolve(PropertyInfo property, IncludeTypeContainer typeContainer)
        {
            typeContainer.IncludeType = typeContainer.IncludeType.GenericTypeArguments[1];
            return GetJoinDictionaryCallback<TKey, TInclude>(property, _includeOperator, _query);
        }

        private Action<TInclude> GetJoinDictionaryCallback<TKey, TInclude>(PropertyInfo property, IncludeResultOperator @operator, ICompiledQuery<TDoc, TOut> query)
        {
            var queryProperty = GetPropertyInfo(property, @operator);

            var storage = _schema.StorageFor(typeof(TInclude));

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