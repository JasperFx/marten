using System;
using System.Linq.Expressions;
using Baseline;
using Marten.Internal;
using Marten.Linq.Fields;
using Marten.Linq.Includes;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;
using Marten.Util;

namespace Marten.Linq.SqlGeneration
{
    public abstract class SelectorStatement: Statement
    {
        protected SelectorStatement(ISelectClause selectClause, IFieldMapping fields) : base(fields)
        {
            SelectClause = selectClause;
            FromObject = SelectClause.FromObject;
        }

        public ISelectClause SelectClause { get; internal set; }

        protected override void configure(CommandBuilder sql)
        {
            startCommonTableExpression(sql);

            SelectClause.WriteSelectClause(sql);

            writeWhereClause(sql);

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

            endCommonTableExpression(sql);
        }



        public bool IsDistinct { get; set; }

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

        public SelectorStatement ToSelectMany(IField collectionField, IMartenSession session, bool isComplex,
            Type elementType)
        {
            if (elementType.IsSimple())
            {
                var selection = $"jsonb_array_elements_text({collectionField.JSONBLocator})";

                SelectClause = typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(SelectClause.FromObject, selection,
                    elementType);

                Mode = StatementMode.CommonTableExpression;
                ExportName = session.NextTempTableName() + "CTE";

                var next = elementType == typeof(string)
                    ? new ScalarSelectManyStringStatement(this)
                    : typeof(ScalarSelectManyStatement<>).CloseAndBuildAs<Statement>(this, session.Serializer, elementType);

                InsertAfter(next);

                return (SelectorStatement) next;
            }

            var childFields = session.Options.ChildTypeMappingFor(elementType);

            if (isComplex)
            {
                var selection = $"jsonb_array_elements({collectionField.JSONBLocator})";
                SelectClause = typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(SelectClause.FromObject, selection,
                    elementType);

                Mode = StatementMode.CommonTableExpression;
                ExportName = session.NextTempTableName() + "CTE";

                var statement = new JsonStatement(elementType, childFields, this);

                InsertAfter(statement);

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

        public void ToSelectTransform(Expression selectExpression, ISerializer serializer)
        {
            var builder = new SelectTransformBuilder(selectExpression, Fields, serializer);
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

        public virtual SelectorStatement UseAsEndOfTempTableAndClone(IncludeIdentitySelectorStatement includeIdentitySelectorStatement)
        {
            throw new NotImplementedException();
        }
    }
}
