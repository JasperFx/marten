using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Baseline.Conversion;
using Marten.Schema;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq.Model
{
    public class SelectManyQuery
    {
        public int Index { get; set; }
        private static readonly Conversions conversions = new Conversions();

        private readonly IField _field;
        private readonly QuerySourceReferenceExpression _expression;
        private readonly Expression _from;
        private readonly IDocumentSchema _schema;
        private readonly QueryModel _query;
        private readonly int _take;


        public SelectManyQuery(IDocumentSchema schema, IQueryableDocument mapping, QueryModel query, int index)
        {
            Index = index;

            _schema = schema;
            _query = query;

            _expression = query.SelectClause.Selector.As<QuerySourceReferenceExpression>();
            _from = _expression.ReferencedQuerySource.As<AdditionalFromClause>().FromExpression;

            var members = FindMembers.Determine(_from);
            _field = mapping.FieldFor(members);

            IsDistinct = query.HasOperator<DistinctResultOperator>();

            var next = query.BodyClauses.Skip(index + 1).FirstOrDefault(x => x is AdditionalFromClause);
            if (next != null)
            {
                throw new NotSupportedException("Not yet supporting SelectMany().SelectMany()");
            }
            else
            {
                _take = _query.BodyClauses.Count - index;
            }

            
        }

        public bool IsComplex => bodyClauses().Any();

        private T findOperator<T>()
        {
            return _query.BodyClauses.Skip(Index).Take(_take).OfType<T>().FirstOrDefault();
        }

        private IEnumerable<T> findOperators<T>()
        {
            return bodyClauses().OfType<T>();
        }

        private IEnumerable<IBodyClause> bodyClauses()
        {
            return _query.BodyClauses.Skip(Index).Take(_take);
        }

        public ISelector<T> ToSelector<T>(ISerializer serializer)
        {
            if (IsComplex)
            {
                return new DeserializeSelector<T>(serializer, $"jsonb_array_elements({_field.SqlLocator}) as x");
            }

            if (typeof(T) == typeof(string))
            {
                return new SingleFieldSelector<T>(IsDistinct, $"jsonb_array_elements_text({_field.SqlLocator}) as x");
            }
            else if (TypeMappings.HasTypeMapping(typeof(T)))
            {
                return new ArrayElementFieldSelector<T>(IsDistinct, _field, conversions);
            }

            return new DeserializeSelector<T>(serializer, $"jsonb_array_elements_text({_field.SqlLocator}) as x");

        }

        public string SqlLocator => _field.SqlLocator;

        public bool IsDistinct { get; }

        public string ConfigureCommand(NpgsqlCommand command, string sql)
        {
            // Look for a select clause
            // Look for where clauses
            // Look for order by clauses
            // Look for Take/Skip

            var docType = _field.MemberType.GetElementType();
            

            var subName = "sub" + Index;
            var document = _schema.StoreOptions.GetChildDocument(subName + ".x", docType);

            var select = "select x from ";
            // TODO -- look for a Select() clause

            sql = $"{select} ({sql}) as {subName}";

            var @where = buildWhereFragment(document);
            if (@where != null)
            {
                sql += " where " + @where.ToSql(command);
            }

            var orderBy = determineOrderClause(document);

            if (orderBy.IsNotEmpty()) sql += orderBy;

            return sql;
        }

        private string determineOrderClause(ChildDocument document)
        {
            var orders = bodyClauses().OfType<OrderByClause>().SelectMany(x => x.Orderings).ToArray();
            if (!orders.Any()) return string.Empty;

            return " order by " + orders.Select(x => toOrderClause(document, x)).Join(", ");
        }

        private string toOrderClause(ChildDocument document, Ordering clause)
        {
            var locator = document.JsonLocator(clause.Expression);
            return clause.OrderingDirection == OrderingDirection.Asc
                ? locator
                : locator + " desc";
        }

        private IWhereFragment buildWhereFragment(ChildDocument document)
        {
            var wheres = findOperators<WhereClause>().ToArray();
            if (!wheres.Any())
            {
                return null;
            }

            return wheres.Length == 1
                ? _schema.Parser.ParseWhereFragment(document, wheres.Single().Predicate)
                : new CompoundWhereFragment(_schema.Parser, document, "and", wheres);
        }
    }
}