using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq.Parsing;
using Marten.Schema;
using Remotion.Linq.Parsing;

namespace Marten.Linq
{
    public class NotVisitor : RelinqExpressionVisitor
    {
        private readonly MartenExpressionParser.WhereClauseVisitor _parent;
        private readonly IQueryableDocument _mapping;
        private readonly Action<IWhereFragment> _callback;
	    private static readonly IMethodCallParser[] _parsers = {
			new SimpleNotEqualsParser(),
			new StringNotEquals(),
			new StringNotContains(),
		    new StringNotStartsWith(),
			new StringNotEndsWith(),		 
            new IsNotOneOf()
        };
	    
        private static readonly object[] _supplementalParsers =
        {
            new SimpleBinaryNotNodeComparisonExpressionParser(),
        };
        private readonly ISerializer _serializer;

	    public NotVisitor(MartenExpressionParser.WhereClauseVisitor parent, IQueryableDocument mapping, Action<IWhereFragment> callback, ISerializer serializer)
        {
            _parent = parent;
            _mapping = mapping;
            _callback = callback;	        
	        _serializer = serializer;
        }

		protected override Expression VisitMethodCall(MethodCallExpression expression)
		{
			var parser = _parsers.FirstOrDefault(x => x.Matches(expression));

			if (parser != null)
			{
				var where = parser.Parse(_mapping, _serializer, expression);
				_callback(where);

				return expression;
			}
	
			return base.VisitMethodCall(expression);
		}

	    protected override Expression VisitMember(MemberExpression expression)
        {
            if (expression.Type == typeof (bool))
            {
                var locator = _mapping.JsonLocator(expression);
                var @where = new WhereFragment($"{locator} != ?", true);
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
                    return base.VisitBinary(expression);
                }
            }

            var parser = _supplementalParsers.OfType<IExpressionParser<BinaryExpression>>()?.FirstOrDefault(x => x.Matches(expression));

            if (parser != null)
            {
                var where = parser.Parse(_mapping, _serializer, expression);
                _callback(where);

                return expression;
            }

            return base.VisitBinary(expression);
        }
    }
}