using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Schema;
using Marten.Services.Includes;
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
                var result = (IIncludeJoin) method.Invoke(this, new object[] {query, includeOperator});
                includeJoins.Add(result);
            }
            return includeJoins.ToArray();
        }

        private static Action<TInclude> GetCallback<TInclude>(PropertyInfo property, IncludeResultOperator @operator, ICompiledQuery<TDoc, TOut> query)
        {
            var target = Expression.Parameter(property.ReflectedType, "target");
            var method = property.GetGetMethod();

            var callGetMethod = Expression.Call(target, method);

            var lambda = Expression.Lambda<Func<IncludeResultOperator, LambdaExpression>>(callGetMethod, target);

            var compiledLambda = lambda.Compile();
            var callback = compiledLambda.Invoke(@operator);
            var mi = ((MemberExpression) callback.Body).Member;
            
            return x => ((PropertyInfo) mi).SetValue(query, x);
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
            var included = _schema.MappingFor(includeType);

            var property = typeof (IncludeResultOperator).GetProperty("Callback");

            return mapping.JoinToInclude(joinType, included, members, GetCallback<TInclude>(property, includeOperator, query));
        }
    }
}