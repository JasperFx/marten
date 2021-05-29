using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing
{


    internal partial class WhereClauseParser : RelinqExpressionVisitor, IWhereFragmentHolder
    {
        private static readonly IDictionary<ExpressionType, string> _operators = new Dictionary<ExpressionType, string>
        {
            {ExpressionType.Equal, "="},
            {ExpressionType.NotEqual, "!="},
            {ExpressionType.GreaterThan, ">"},
            {ExpressionType.GreaterThanOrEqual, ">="},
            {ExpressionType.LessThan, "<"},
            {ExpressionType.LessThanOrEqual, "<="}
        };


        private readonly IMartenSession _session;
        private readonly Statement _statement;
        private IWhereFragmentHolder _holder;

        public WhereClauseParser(IMartenSession session, Statement statement)
        {
            _session = session;
            _statement = statement;
            _holder = this;
        }

        public ISqlFragment Build(WhereClause clause)
        {
            _holder = this;
            Where = null;

            Visit(clause.Predicate);

            if (Where == null)
            {
                throw new BadLinqExpressionException($"Unsupported Where clause: '{clause.Predicate}'");
            }

            return Where;
        }

        public ISqlFragment Where { get; private set; }
        public bool InSubQuery { get; set; }

        void IWhereFragmentHolder.Register(ISqlFragment fragment)
        {
            Where = fragment;
        }

        protected override Expression VisitSubQuery(SubQueryExpression expression)
        {
            var parser = new SubQueryFilterParser(this, expression);
            var where = parser.BuildWhereFragment();
            _holder.Register(where);

            return null;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var where = _session.Options.Linq.BuildWhereFragment(_statement.Fields, node, _session.Serializer);
            _holder.Register(where);

            return null;
        }


        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (_operators.TryGetValue(node.NodeType, out var op))
            {
                var binary = new BinaryExpressionVisitor(this);
                _holder.Register(binary.BuildWhereFragment(node, op));

                return null;
            }

            switch (node.NodeType)
            {
                case ExpressionType.AndAlso:
                    buildCompoundWhereFragment(node, "and");
                    break;

                case ExpressionType.OrElse:
                    buildCompoundWhereFragment(node, "or");
                    break;

                default:
                    throw new BadLinqExpressionException($"Unsupported expression type '{node.NodeType}' in binary expression");
            }


            return null;
        }

        private void buildCompoundWhereFragment(BinaryExpression node, string separator)
        {
            var original = _holder;

            var compound =  CompoundWhereFragment.For(separator);
            _holder.Register(compound);

            _holder = compound;

            Visit(node.Left);

            _holder = compound;

            Visit(node.Right);

            _holder = original;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Not)
            {
                var original = _holder;
                _holder = new NotWhereFragment(original);
                var returnValue = Visit(node.Operand);

                _holder = original;

                return returnValue;
            }

            return base.VisitUnary(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Type == typeof(bool))
            {
                var field = _statement.Fields.FieldFor(node);
                _holder.Register(new BooleanFieldIsTrue(field));
                return null;
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if ((node.Type == typeof(bool)))
            {
                if (InSubQuery)
                {
                    throw new BadLinqExpressionException("Unsupported Where() clause in a sub-collection expression");
                }

                _holder.Register(new WhereFragment(node.Value.ToString().ToLower()));
            }

            return base.VisitConstant(node);
        }


        internal enum SubQueryUsage
        {
            Any,
            Count,
            Contains,
            Intersect
        }

        internal class SubQueryFilterParser : RelinqExpressionVisitor
        {
            private readonly WhereClauseParser _parent;
            private readonly SubQueryExpression _expression;
#pragma warning disable 414
            private bool _isDistinct;
#pragma warning restore 414
            private readonly WhereClause[] _wheres;
            private readonly Expression _contains;

            public SubQueryFilterParser(WhereClauseParser parent, SubQueryExpression expression)
            {
                _parent = parent;
                _expression = expression;

                foreach (var @operator in expression.QueryModel.ResultOperators)
                {
                    switch (@operator)
                    {
                        case AnyResultOperator _:
                            Usage = SubQueryUsage.Any;

                            break;

                        case CountResultOperator _:
                            Usage = SubQueryUsage.Count;
                            break;

                        case LongCountResultOperator _:
                            Usage = SubQueryUsage.Count;
                            break;

                        case DistinctResultOperator _:
                            _isDistinct = true;
                            break;

                        case ContainsResultOperator op:
                            Usage = op.Item is QuerySourceReferenceExpression
                                ? SubQueryUsage.Intersect
                                : SubQueryUsage.Contains;

                            _contains = op.Item;
                            break;

                        default:
                            throw new BadLinqExpressionException($"Invalid result operator {@operator} in sub query '{expression}'");
                    }
                }

                _wheres = expression.QueryModel.BodyClauses.OfType<WhereClause>().ToArray();
            }

            public SubQueryUsage Usage { get;  }

            public ISqlFragment BuildWhereFragment()
            {
                switch (Usage)
                {
                    case SubQueryUsage.Any:
                        return buildWhereForAny(findArrayField());

                    case SubQueryUsage.Contains:
                        return buildWhereForContains(findArrayField());

                    case SubQueryUsage.Intersect:
                        return new WhereInArrayFilter("data", (ConstantExpression)_expression.QueryModel.MainFromClause.FromExpression);

                    default:
                        throw new NotSupportedException();
                }
            }

            private ArrayField findArrayField()
            {
                ArrayField field;
                try
                {
                    field = (ArrayField) _parent._statement.Fields.FieldFor(_expression.QueryModel.MainFromClause.FromExpression);
                }
                catch (Exception e)
                {
                    throw new BadLinqExpressionException("The sub query is not sourced from a supported collection type", e);
                }

                return field;
            }

            private ISqlFragment buildWhereForContains(ArrayField field)
            {
                if (_contains is ConstantExpression c)
                {
                    var flattened = new FlattenerStatement(field, _parent._session, _parent._statement);
                    var idSelectorStatement = new ContainsIdSelectorStatement(flattened, _parent._session, c);
                    return new WhereInSubQuery(idSelectorStatement.ExportName);
                }

                throw new NotSupportedException();
            }

            private ISqlFragment buildWhereForAny(ArrayField field)
            {
                if (_wheres.Any())
                {
                    var flattened = new FlattenerStatement(field, _parent._session, _parent._statement);


                    var itemType = _expression.QueryModel.MainFromClause.ItemType;
                    var elementFields =
                        _parent._session.Options.ChildTypeMappingFor(itemType);

                    var idSelectorStatement = new IdSelectorStatement(_parent._session, elementFields, flattened);
                    idSelectorStatement.WhereClauses.AddRange(_wheres);
                    idSelectorStatement.CompileLocal(_parent._session);

                    return new WhereInSubQuery(idSelectorStatement.ExportName);
                }

                return new CollectionIsNotEmpty(field);
            }


            public CountComparisonStatement BuildCountComparisonStatement()
            {
                if (Usage != SubQueryUsage.Count)
                {
                    throw new BadLinqExpressionException("Invalid comparison");
                }

                var field = findArrayField();
                var flattened = new FlattenerStatement(field, _parent._session, _parent._statement);

                var elementFields =
                    _parent._session.Options.ChildTypeMappingFor(field.ElementType);
                var statement = new CountComparisonStatement(_parent._session, field.ElementType, elementFields, flattened);
                if (_wheres.Any())
                {
                    statement.WhereClauses.AddRange(_wheres);
                    statement.CompileLocal(_parent._session);
                }

                return statement;
            }

        }


    }
}
