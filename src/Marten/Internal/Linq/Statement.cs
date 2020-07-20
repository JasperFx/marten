using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Linq.QueryHandlers;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Util;
using Remotion.Linq.Clauses;

namespace Marten.Internal.Linq
{
    public enum StatementMode
    {
        Select,
        CommonTableExpression
    }

    public abstract class Statement
    {
        private Statement _next;

        protected Statement(ISelectClause selectClause, IFieldMapping fields)
        {
            SelectClause = selectClause;
            Fields = fields;
        }

        public Statement Previous { get; internal set; }

        public Statement Next
        {
            get => _next;
            internal set
            {
                _next = value ?? throw new ArgumentNullException(nameof(value));
                value.Previous = this;
            }
        }

        public StatementMode Mode { get; set; } = StatementMode.Select;

        /// <summary>
        /// For CTEs
        /// </summary>
        public string ExportName { get; protected set; }

        public void Configure(CommandBuilder sql)
        {
            configure(sql);
            if (Next != null)
            {
                sql.Append(" ");
                Next.Configure(sql);
            }
        }

        public ISelectClause SelectClause { get; internal set; }
        public IList<Ordering> Orderings { get; protected set; } = new List<Ordering>();
        public IFieldMapping Fields { get; }

        public IList<WhereClause> WhereClauses { get; } = new List<WhereClause>();

        protected virtual void configure(CommandBuilder sql)
        {
            if (Mode == StatementMode.CommonTableExpression)
            {
                sql.Append(Previous == null ? "WITH " : " , ");

                sql.Append(ExportName);
                sql.Append(" as (\n");
            }

            SelectClause.WriteSelectClause(sql);

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
            if (!WhereClauses.Any()) return null;

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

        public IWhereFragment Where { get; internal set; }
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

        public IQueryHandler<TResult> BuildSingleResultHandler<TResult>(IMartenSession session, Statement topStatement)
        {
            var selector = (ISelector<TResult>)SelectClause.BuildSelector(session);
            return new OneResultHandler<TResult>(topStatement, selector, ReturnDefaultWhenEmpty, CanBeMultiples);
        }

        public void ToScalar(Expression selectClauseSelector)
        {
            var field = Fields.FieldFor(selectClauseSelector);

            SelectClause = field.FieldType == typeof(string)
                ? new ScalarStringSelectClause(field, SelectClause.FromObject)
                : typeof(ScalarSelectClause<>).CloseAndBuildAs<ISelectClause>(field, SelectClause.FromObject, field.FieldType);
        }

        public Statement ToSelectMany(IField collectionField, IMartenSession session, bool isComplex,
            Type elementType)
        {
            if (elementType.IsSimple())
            {
                var selection = $"jsonb_array_elements_text({collectionField.JSONBLocator})";

                SelectClause = typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(SelectClause.FromObject, selection,
                    elementType);

                Mode = StatementMode.CommonTableExpression;
                ExportName = elementType.Name.Sanitize() + "CTE";

                Next = elementType == typeof(string)
                    ? new ScalarSelectManyStringStatement(this)
                    : typeof(ScalarSelectManyStatement<>).CloseAndBuildAs<Statement>(this, session.Serializer, elementType);

                return Next;
            }

            var childFields = session.Options.ChildTypeMappingFor(elementType);

            if (isComplex)
            {
                var selection = $"jsonb_array_elements({collectionField.JSONBLocator})";
                SelectClause = typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(SelectClause.FromObject, selection,
                    elementType);

                Mode = StatementMode.CommonTableExpression;
                ExportName = elementType.Name + "CTE";

                var statement = new JsonStatement(elementType, childFields, this)
                {
                    Previous = this
                };

                Next = statement;

                return statement;

            }
            else
            {
                var selection = $"jsonb_array_elements_text({collectionField.JSONBLocator})";
                SelectClause = typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(SelectClause.FromObject, selection,
                    elementType);

                return this;
            }
        }

        public void ApplySqlOperator(string databaseOperator)
        {
            if (SelectClause is IScalarSelectClause c)
            {
                c.ApplyOperator(databaseOperator);

                // Hack, but let it go
                if (databaseOperator == "AVG")
                {
                    SelectClause = c.CloneToDouble();
                }
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

        public void ToSelectTransform(Expression selectExpression)
        {
            var builder = new SelectTransformBuilder(selectExpression, Fields);
            var transformField = builder.SelectedFieldExpression;

            SelectClause = typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(SelectClause.FromObject, transformField, selectExpression.Type);
        }


        public void UseStatistics(QueryStatistics statistics)
        {
            SelectClause = SelectClause.UseStatistics(statistics);
        }

        public void ToJsonSelector()
        {
            SelectClause = new JsonSelectClause(SelectClause);
        }


    }
}
