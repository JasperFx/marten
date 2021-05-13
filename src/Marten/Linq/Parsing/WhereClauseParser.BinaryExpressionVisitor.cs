using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Fields;
using Marten.Linq.SqlGeneration;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing
{
    internal partial class WhereClauseParser
    {
        internal class BinaryExpressionVisitor: RelinqExpressionVisitor
        {
            private readonly WhereClauseParser _parent;
            private BinarySide _left;
            private BinarySide _right;

            public BinaryExpressionVisitor(WhereClauseParser parent)
            {
                _parent = parent;
            }

            public ISqlFragment BuildWhereFragment(BinaryExpression node, string op)
            {
                _left = analyze(node.Left);
                _right = analyze(node.Right);

                return _left.CompareTo(_right, op);
            }

            private BinarySide analyze(Expression expression)
            {
                switch (expression)
                {
                    case ConstantExpression c:
                        return new BinarySide(expression)
                        {
                            Constant = c
                        };
                    case PartialEvaluationExceptionExpression p:
                    {
                        var inner = p.Exception;

                        throw new BadLinqExpressionException($"Error in value expression inside of the query for '{p.EvaluatedExpression}'. See the inner exception:", inner);
                    }
                    case SubQueryExpression subQuery:
                    {
                        var parser = new SubQueryFilterParser(_parent, subQuery);

                        return new BinarySide(expression)
                        {
                            Comparable = parser.BuildCountComparisonStatement()
                        };
                    }
                    case QuerySourceReferenceExpression source:
                        return new BinarySide(expression)
                        {
                            Field = new SimpleDataField(source.Type)
                        };
                    case BinaryExpression {NodeType: ExpressionType.Modulo} binary:
                        return new BinarySide(expression){Comparable = new ModuloFragment(binary, _parent._statement.Fields)};

                    case BinaryExpression {NodeType: ExpressionType.NotEqual} ne:
                        if (ne.Right is ConstantExpression v && v.Value == null)
                        {
                            var field = _parent._statement.Fields.FieldFor(ne.Left);
                            return new BinarySide(expression)
                            {
                                Comparable = new HasValueField(field)
                            };
                        }

                        throw new BadLinqExpressionException($"Invalid Linq Where() clause with expression: " + ne);
                    case BinaryExpression binary:
                        throw new BadLinqExpressionException($"Unsupported nested operator '{binary.NodeType}' as an operand in a binary expression");
                    case UnaryExpression u when u.NodeType == ExpressionType.Not:
                        return new BinarySide(expression){Comparable = new NotField(_parent._statement.Fields.FieldFor(u.Operand))};
                    default:
                        return new BinarySide(expression){Field = _parent._statement.Fields.FieldFor(expression)};
                }
            }
        }
    }
}
