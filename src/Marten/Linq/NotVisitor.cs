using System;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema;
using Remotion.Linq.Parsing;

namespace Marten.Linq
{
    public class NotVisitor : RelinqExpressionVisitor
    {
        private readonly MartenExpressionParser.WhereClauseVisitor _parent;
        private readonly IQueryableDocument _mapping;
        private readonly Action<IWhereFragment> _callback;

        public NotVisitor(MartenExpressionParser.WhereClauseVisitor parent, IQueryableDocument mapping, Action<IWhereFragment> callback)
        {
            _parent = parent;
            _mapping = mapping;
            _callback = callback;
        }

        protected override Expression VisitMember(MemberExpression expression)
        {
            if (expression.Type == typeof (bool))
            {
                var locator = _mapping.JsonLocator(expression);
                var @where = new WhereFragment($"{locator} = False");
                _callback(@where);
            }

            return base.VisitMember(expression);
        }

        protected override Expression VisitBinary(BinaryExpression expression)
        {
            if (expression.Type == typeof (bool) && expression.NodeType == ExpressionType.NotEqual)
            {
                var binaryExpression = expression.As<BinaryExpression>();
                var locator = _mapping.JsonLocator(binaryExpression.Left);
                if (binaryExpression.Right.NodeType == ExpressionType.Constant &&
                    binaryExpression.Right.As<ConstantExpression>().Value == null)
                {
                    var @where = new WhereFragment($"({locator}) IS NULL");
                    _callback(@where);
                }
            }

            return base.VisitBinary(expression);
        }
    }
}