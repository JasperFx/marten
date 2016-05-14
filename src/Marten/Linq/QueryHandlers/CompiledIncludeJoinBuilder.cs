using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Schema;
using Marten.Services.Includes;
using Marten.Util;
using Remotion.Linq;

namespace Marten.Linq.QueryHandlers
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

        private static Action<TInclude> GetJoinCallback<TInclude>(PropertyInfo property, IncludeResultOperator @operator, ICompiledQuery<TDoc, TOut> query)
        {
            var queryProperty = GetPropertyInfo(property, @operator);

            return x => queryProperty.SetValue(query, x);
        }

        private static Action<TInclude> GetJoinListCallback<TInclude>(PropertyInfo property, IncludeResultOperator @operator, ICompiledQuery<TDoc, TOut> query)
        {
            var queryProperty = GetPropertyInfo(property, @operator);

            var included = (IList<TInclude>) (queryProperty).GetValue(query);
            if (included == null)
            {
                queryProperty.SetValue(query, new List<TInclude>());
                included = (IList<TInclude>)queryProperty.GetValue(query);
            }

            return included.Fill;
        }

        private Action<TInclude> GetJoinDictionaryCallback<TKey, TInclude>(PropertyInfo property, IncludeResultOperator @operator, ICompiledQuery<TDoc, TOut> query)
        {
            var queryProperty = GetPropertyInfo(property, @operator);

            var storage = _schema.StorageFor(typeof(TInclude));

            var dictionary = (IDictionary<TKey,TInclude>) (queryProperty).GetValue(query);
            if (dictionary == null)
            {
                queryProperty.SetValue(query, new Dictionary<TKey,TInclude>());
                dictionary = (IDictionary<TKey,TInclude>)queryProperty.GetValue(query);
            }

            return x => {
                var id = storage.Identity(x).As<TKey>();
                if (!dictionary.ContainsKey(id))
                {
                    dictionary.Add(id, x);
                }
            };
        }

        private static PropertyInfo GetPropertyInfo(PropertyInfo property, IncludeResultOperator @operator)
        {
            var target = Expression.Parameter(property.ReflectedType, "target");
            var method = property.GetGetMethod();

            var callGetMethod = Expression.Call(target, method);

            var lambda = Expression.Lambda<Func<IncludeResultOperator, LambdaExpression>>(callGetMethod, target);

            var compiledLambda = lambda.Compile();
            var callback = compiledLambda.Invoke(@operator);
            var mi = (PropertyInfo) ((MemberExpression) callback.Body).Member;
            return mi;
        }

        private IIncludeJoin GetDictionaryJoin<TKey,TInclude>(ICompiledQuery<TDoc, TOut> query, IncludeResultOperator includeOperator) where TInclude : class
        {
            var idSource = includeOperator.IdSource as Expression<Func<TDoc, object>>;
            var joinType = (JoinType)includeOperator.JoinType.Value;

            var visitor = new FindMembers();
            visitor.Visit(idSource);
            var members = visitor.Members.ToArray();

            var mapping = _schema.MappingFor(typeof(TDoc));
            var includeType = includeOperator.Callback.Body.Type;

            var property = typeof (IncludeResultOperator).GetProperty("Callback");

            Action<TInclude> callback;
            includeType = includeType.GenericTypeArguments[1];
            callback = GetJoinDictionaryCallback<TKey, TInclude>(property, includeOperator, query);

            var included = _schema.MappingFor(includeType);

            return mapping.JoinToInclude(joinType, included, members, callback);
        }

        private IIncludeJoin GetJoin<TInclude>(ICompiledQuery<TDoc, TOut> query, IncludeResultOperator includeOperator) where TInclude : class
        {
            var idSource = includeOperator.IdSource as Expression<Func<TDoc, object>>;
            var joinType = (JoinType)includeOperator.JoinType.Value;

            var visitor = new FindMembers();
            visitor.Visit(idSource);
            var members = visitor.Members.ToArray();

            var mapping = _schema.MappingFor(typeof(TDoc));
            var includeType = includeOperator.Callback.Body.Type;

            var property = typeof (IncludeResultOperator).GetProperty("Callback");

            Action<TInclude> callback;
            if (includeType.IsGenericEnumerable())
            {
                includeType = includeType.GenericTypeArguments[0];
                callback = GetJoinListCallback<TInclude>(property, includeOperator, query);
            }
            else
            {
                callback = GetJoinCallback<TInclude>(property, includeOperator, query);
            }

            var included = _schema.MappingFor(includeType);

            return mapping.JoinToInclude(joinType, included, members, callback);
        }
    }
}