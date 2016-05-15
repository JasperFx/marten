using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Schema;
using Marten.Services.Includes;
using Marten.Util;
using Remotion.Linq;

namespace Marten.Linq.QueryHandlers.CompiledInclude
{
    public class CompiledIncludeJoinBuilder<TDoc, TOut>
    {
        private readonly IDocumentSchema _schema;

        public CompiledIncludeJoinBuilder(IDocumentSchema schema)
        {
            _schema = schema;
        }

        public IIncludeJoin[] BuildIncludeJoins(QueryModel model, ICompiledQuery<TDoc, TOut> query)
        {
            var includeOperators = model.FindOperators<IncludeResultOperator>();
            var includeJoins = new List<IIncludeJoin>();
            foreach (var includeOperator in includeOperators)
            {
                var includeType = includeOperator.Callback.Body.Type;
                var method = GetType().GetMethod(nameof(GetJoin), BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(includeType);
                if (includeType.IsGenericDictionary())
                {
                    var tkey = includeType.GenericTypeArguments[0];
                    var tinclude = includeType.GenericTypeArguments[1];
                    method = GetType().GetMethod(nameof(GetDictionaryJoin), BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(tkey, tinclude);
                }
                else if (includeType.IsGenericEnumerable())
                {
                    includeType = includeType.GenericTypeArguments[0];
                    method = GetType().GetMethod(nameof(GetJoin), BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(includeType);
                }
                var result = (IIncludeJoin) method.Invoke(this, new object[] {query, includeOperator});
                includeJoins.Add(result);
            }
            return includeJoins.ToArray();
        }

        private IIncludeJoin GetDictionaryJoin<TKey,TInclude>(ICompiledQuery<TDoc, TOut> query, IncludeResultOperator includeOperator) where TInclude : class
        {
            var resolver = new DictionaryIncludeCallbackResolver<TKey, TInclude, TDoc, TOut>(query, includeOperator, _schema);
            return doGetJoin<TInclude>(query, includeOperator, resolver);
        }

        private IIncludeJoin GetJoin<TInclude>(ICompiledQuery<TDoc, TOut> query, IncludeResultOperator includeOperator) where TInclude : class
        {
            var resolver = new DefaultIncludeCallbackResolver<TInclude,TDoc,TOut>(query, includeOperator);
            return doGetJoin(query, includeOperator, resolver);
        }

        private IIncludeJoin doGetJoin<TInclude>(ICompiledQuery<TDoc, TOut> query, IncludeResultOperator includeOperator, IIncludeCallbackResolver<TInclude> callbackResolver) where TInclude : class
        {
            var idSource = includeOperator.IdSource as Expression<Func<TDoc, object>>;
            var joinType = (JoinType)includeOperator.JoinType.Value;

            var visitor = new FindMembers();
            visitor.Visit(idSource);
            var members = visitor.Members.ToArray();

            var mapping = _schema.MappingFor(typeof(TDoc));
            var typeContainer = new IncludeTypeContainer {IncludeType = includeOperator.Callback.Body.Type};

            var property = typeof (IncludeResultOperator).GetProperty("Callback");

            var callback = callbackResolver.Resolve(property, typeContainer);

            var included = _schema.MappingFor(typeContainer.IncludeType);

            return mapping.JoinToInclude(joinType, included, members, callback);
        }
    }

    public class IncludeTypeContainer
    {
        public Type IncludeType { get; set; }
    }
}