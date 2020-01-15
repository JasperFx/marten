using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Util;
using Remotion.Linq.Parsing;

namespace Marten.Linq.Compiled
{
    public class ContainmentParameterVisitor: RelinqExpressionVisitor
    {
        private readonly ISerializer _serializer;
        private readonly Type _queryType;
        private readonly IList<IDbParameterSetter> _parameterSetters = new List<IDbParameterSetter>();

        private Type _elementType;
        private readonly IList<MemberInfo> _collectionMembers = new List<MemberInfo>();

        public ContainmentParameterVisitor(ISerializer serializer, Type queryType, IList<IDbParameterSetter> parameterSetters)
        {
            _serializer = serializer;
            _queryType = queryType;
            _parameterSetters = parameterSetters;
        }

        private IContainmentParameterSetter addParameter()
        {
            var param = typeof(ContainmentParameterSetter<>).CloseAndBuildAs<IContainmentParameterSetter>(_serializer, _collectionMembers.ToArray(), _queryType);
            _parameterSetters.Add(param);

            return param;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            _elementType = _collectionMembers.Last().GetMemberType().GetTypeInfo().GenericTypeArguments.First();

            return base.VisitParameter(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (_elementType == null)
            {
                _collectionMembers.Insert(0, node.Member);
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType == ExpressionType.Equal)
            {
                var param = _parameterSetters.OfType<IContainmentParameterSetter>().LastOrDefault() ?? addParameter();

                var members = FindMembers.Determine(node.Left);
                var keys = members.Select(x => x.Name).ToArray();

                if (node.Right.NodeType == ExpressionType.Constant)
                {
                    param.Constant(keys, node.Right.As<ConstantExpression>().Value);
                }
                else
                {
                    var member = FindMembers.Determine(node.Right).LastOrDefault();
                    if (member == null)
                    {
                        throw new NotSupportedException("Marten does not yet support this type of child collection querying");
                    }

                    param.AddElement(keys, member);
                }
            }

            return base.VisitBinary(node);
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            return base.VisitUnary(node);
        }
    }
}
