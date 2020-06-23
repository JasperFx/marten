using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Schema;
using Marten.Util;
using Marten.V4Internals.Linq.QueryHandlers;
using Remotion.Linq.Clauses;

namespace Marten.V4Internals.Linq
{
    public enum StatementMode
    {
        Select,
        CommonTableExpression
    }

    public abstract class Statement
    {
        protected Statement(ISelectClause selectClause, IFieldMapping fields)
        {
            SelectClause = selectClause;
            Fields = fields;
        }

        public Statement Previous { get; protected set; }
        public Statement Next { get; protected set; }

        public StatementMode Mode { get; set; } = StatementMode.Select;

        /// <summary>
        /// For CTEs
        /// </summary>
        public string ExportName { get; protected set; }

        public void Configure(CommandBuilder sql, bool withStatistics)
        {
            configure(sql, withStatistics);
            if (Next != null)
            {
                sql.Append(" ");
                Next.Configure(sql, withStatistics);
            }
        }

        public ISelectClause SelectClause { get; private set; }
        public IList<Ordering> Orderings { get; } = new List<Ordering>();
        public IFieldMapping Fields { get; }

        public IList<WhereClause> WhereClauses { get; } = new List<WhereClause>();

        protected virtual void configure(CommandBuilder sql, bool withStatistics)
        {
            if (Mode == StatementMode.CommonTableExpression)
            {
                sql.Append(Previous == null ? "WITH " : " , ");

                sql.Append(ExportName);
                sql.Append(" as (\n");
            }

            SelectClause.WriteSelectClause(sql, withStatistics);

            if (Where != null)
            {
                sql.Append(" where ");
                Where.Apply(sql);
            }

            writeOrderClause(sql);

            if (Offset > 0)
            {
                // TODO -- need to add more overloads to avoid the type to DbType lookup
                var param = sql.AddParameter(Offset);
                sql.Append(" OFFSET :");
                sql.Append(param.ParameterName);
            }

            if (Limit > 0)
            {
                var param = sql.AddParameter(Limit);
                sql.Append(" LIMIT :");
                sql.Append(param.ParameterName);
            }

            if (Mode == StatementMode.CommonTableExpression)
            {
                sql.Append("\n)\n");
            }
        }

        public int Offset { get; set; }
        public int Limit { get; set; }

        protected void writeOrderByFragment(CommandBuilder sql, Ordering clause)
        {
            var locator = Fields.FieldFor(clause.Expression).TypedLocator;
            sql.Append(locator);

            if (clause.OrderingDirection == OrderingDirection.Desc)
            {
                sql.Append(" desc");
            }
        }

        protected virtual IWhereFragment buildWhereFragment(MartenExpressionParser parser)
        {
            return WhereClauses.Count == 1
                ? parser.ParseWhereFragment(Fields, WhereClauses.Single().Predicate)
                : new CompoundWhereFragment(parser, Fields, "and", WhereClauses);
        }

        protected void writeOrderClause(CommandBuilder sql)
        {
            if (Orderings.Any())
            {
                sql.Append(" order by ");
                writeOrderByFragment(sql, Orderings[0]);
                for (var i = 1; i < Orderings.Count; i++)
                {
                    sql.Append(", ");
                    writeOrderByFragment(sql, Orderings[i]);
                }
            }
        }

        public void CompileStructure(MartenExpressionParser parser)
        {
            Where = buildWhereFragment(parser);
            Next?.CompileStructure(parser);
        }

        public IWhereFragment Where { get; private set; }
        public bool SingleValue { get; set; }
        public bool ReturnDefaultWhenEmpty { get; set; }
        public bool CanBeMultiples { get; set; }

        public void ToAny()
        {
            SelectClause = new AnySelectClause(SelectClause.FromObject);
        }

        public void ToCount<T>()
        {
            SelectClause = new CountClause<T>(SelectClause.FromObject);
        }

        public IQueryHandler<TResult> BuildSingleResultHandler<TResult>(IMartenSession session)
        {
            var selector = (ISelector<TResult>)SelectClause.BuildSelector(session);
            return new OneResultHandler<TResult>(this, selector, ReturnDefaultWhenEmpty, CanBeMultiples);
        }

        public void ToScalar(Expression selectClauseSelector)
        {
            var field = Fields.FieldFor(selectClauseSelector);
            SelectClause = typeof(ScalarSelectClause<>).CloseAndBuildAs<ISelectClause>(field, SelectClause.FromObject, field.FieldType);
        }

        public Statement ToSelectMany(IField collectionField, DocumentMapping childFields, bool isComplex)
        {
            if (isComplex)
            {
                var selection = $"select jsonb_array_elements({collectionField.JSONBLocator}) as data from ";
                SelectClause = typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(SelectClause.FromObject, selection,
                    childFields.DocumentType);

                Mode = StatementMode.CommonTableExpression;
                ExportName = childFields.DocumentType.Name + "CTE";

                var statement = new JsonStatement(childFields.DocumentType, childFields, this)
                {
                    Previous = this
                };

                Next = statement;

                return statement;

            }
            else
            {
                var selection = $"select jsonb_array_elements_text({collectionField.JSONBLocator}) as data from ";
                SelectClause = typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(SelectClause.FromObject, selection,
                    childFields.DocumentType);

                return this;
            }
        }

        public void ApplySqlOperator(string databaseOperator)
        {
            if (SelectClause is IScalarSelectClause c)
            {
                c.ApplyOperator(databaseOperator);
            }
            else
            {
                throw new NotSupportedException($"The database operator '{databaseOperator}' cannot be used with non-simple types");
            }
        }

        public void ApplyAggregateOperator(string databaseOperator)
        {
            ApplySqlOperator(databaseOperator);
            SingleValue = true;
            ReturnDefaultWhenEmpty = true;
        }

        public void ToSelectTransform(SelectClause select)
        {
            var builder = new SelectTransformBuilder(select, Fields);
            var transformField = builder.SelectedFieldExpression;

            var selectionText = $"select {transformField} from ";
            SelectClause = typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(SelectClause.FromObject, selectionText, select.Selector.Type);
        }
    }
}
