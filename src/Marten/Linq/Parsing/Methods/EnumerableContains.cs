using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Members;
using Marten.Linq.QueryHandlers;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class EnumerableContains: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        var member = expression.Object ?? expression.Arguments[0];

        if (expression.Method.Name != LinqConstants.CONTAINS)
        {
            return false;
        }

        var elementType = member.Type.IsArray ? member.Type.GetElementType() : member.Type.GetGenericArguments()[0];
        if (!elementType.IsValueTypeForQuerying())
        {
            return false;
        }

        return expression.Arguments.Last().Type == elementType;
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var visitor = new EnumerableContainsVisitor(options.Serializer(), memberCollection);
        visitor.Visit(expression);

        return visitor.Where;
    }

    [Obsolete("Factor this into SimpleExpression. Replace as part of https://github.com/JasperFx/marten/issues/2703")]
    internal class EnumerableContainsVisitor: ExpressionVisitor
    {
        private readonly IQueryableMemberCollection _memberCollection;
        private readonly ISerializer _serializer;
        public readonly List<MemberInfo> Members = new();

        public EnumerableContainsVisitor(ISerializer serializer, IQueryableMemberCollection memberCollection)
        {
            _serializer = serializer;
            _memberCollection = memberCollection;
        }

        public ISqlFragment Where { get; set; }

        public object Values { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Object.TryToParseConstant(out var constant))
            {
                // It's a Contains method on a constant
                var member = _memberCollection.MemberFor(node.Arguments[0]);

                Where = new WhereFragment($"{member.TypedLocator} = ANY(?)", constant.Value);
            }
            else if (node.Arguments.Last() is ParameterExpression &&
                     _memberCollection is ValueCollectionMember valueCollection)
            {
                // list.Contains(one of the element values). This is an array intersect
                var value = node.Arguments[0].Value();

                Where = new WhereInArrayFilter("data", Expression.Constant(value));
            }
            else
            {
                // it's the Count() extension method against a member
                var member = node.Object ?? node.Arguments[0];
                var value = node.Arguments.Last().Value();
                Where = ContainmentWhereFragment.SimpleArrayContains(MemberFinder.Determine(member), _serializer,
                    node.Arguments.Last(), value);
            }

            return null;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            // TODO -- consider using BinarySide???
            if (node.IsCompilableExpression())
            {
                Values = node.ReduceToConstant().Value;
                return null;
            }

            Members.Insert(0, node.Member);

            if (node.Expression == null)
            {
                return null;
            }

            return Visit(node.Expression);
        }
    }
}
