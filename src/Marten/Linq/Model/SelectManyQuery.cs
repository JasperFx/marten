using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Baseline.Conversion;
using Marten.Schema;
using Marten.Services.Includes;
using Marten.Util;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq.Model
{
    public class SelectManyQuery
    {
        private static readonly Conversions conversions = new Conversions();
        private readonly ChildDocument _document;
        private readonly Type _documentType;

        private readonly IField _field;
        private readonly AdditionalFromClause _from;
        private readonly QueryModel _query;
        private readonly DocumentStore _store;
        private readonly string _tableAlias;
        private readonly int _take;

        public SelectManyQuery(DocumentStore store, IQueryableDocument mapping, QueryModel query, int index)
        {
            Index = index;

            _store = store;
            _query = query;

            _from = query.BodyClauses[index - 1].As<AdditionalFromClause>();

            var members = FindMembers.Determine(_from.FromExpression);
            _field = mapping.FieldFor(members);

            IsDistinct = query.HasOperator<DistinctResultOperator>();

            var next = query.BodyClauses.Skip(index + 1).FirstOrDefault(x => x is AdditionalFromClause);
            if (next != null)
                throw new NotSupportedException("Not yet supporting SelectMany().SelectMany()");
            _take = _query.BodyClauses.Count - index;

            _tableAlias = "sub" + Index;
            _documentType = _field.MemberType.DeriveElementType();
            _document = _store.Options.GetChildDocument(_tableAlias + ".x", _documentType);
        }

        public int Index { get; set; }

        public string SqlLocator => _field.SqlLocator;

        public bool IsDistinct { get; }

        public bool IsComplex(IIncludeJoin[] joins) => joins.Any() || bodyClauses().Any() || HasSelectTransform();

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

        public ISelector<T> ToSelector<T>(ISerializer serializer, IIncludeJoin[] joins)
        {
            if (IsComplex(joins))
            {
                if (HasSelectTransform())
                {
                    var visitor = new SelectorParser(_query);
                    visitor.Visit(_query.SelectClause.Selector);

                    return visitor.ToSelector<T>("x", _store.Tenancy.Default, _document);
                }

                if (typeof(T) == typeof(string))
                    return (ISelector<T>)new JsonSelector();

                if (typeof(T) != _documentType)
                    return null;

                return new DeserializeSelector<T>(serializer, RawChildElementField());
            }

            if (typeof(T) == typeof(string))
                return new SingleFieldSelector<T>(IsDistinct, $"jsonb_array_elements_text({_field.SqlLocator}) as x");
            if (TypeMappings.HasTypeMapping(typeof(T)))
                return new ArrayElementFieldSelector<T>(IsDistinct, _field, conversions);

            return new DeserializeSelector<T>(serializer, $"jsonb_array_elements_text({_field.SqlLocator}) as x");
        }

        public string RawChildElementField()
        {
            return $"jsonb_array_elements({_field.SqlLocator}) as x";
        }

        public bool HasSelectTransform()
        {
            return _query.SelectClause.Selector.Type != _documentType;
        }

        public void ConfigureCommand(IIncludeJoin[] joins, ISelector selector, CommandBuilder sql, int limit)
        {
            var innerSql = sql.ToString();
            sql.Clear();

            var fields = selector.SelectFields().ToArray();

            if (HasSelectTransform())
            {
                sql.Append("select ");
                sql.Append(fields[0]);
                sql.Append(" from (");
                sql.Append(innerSql);
                sql.Append(") as ");
                sql.Append(_tableAlias);
            }
            else
            {
                fields[0] = "x";

                sql.Append("select ");
                sql.Append(fields[0]);

                for (var i = 1; i < fields.Length; i++)
                {
                    sql.Append(", ");
                    sql.Append(fields[i]);
                }

                sql.Append(" from (");
                sql.Append(innerSql);
                sql.Append(") as ");
                sql.Append(_tableAlias);
            }

            if (joins.Any())
                foreach (var join in joins)
                {
                    sql.Append(" ");
                    join.AppendJoin(sql, _tableAlias, _document);
                }

            var where = buildWhereFragment(_document);
            if (where != null)
            {
                sql.Append(" where ");
                where.Apply(sql);
            }

            var orderBy = determineOrderClause(_document);

            if (orderBy.IsNotEmpty())
                sql.Append(orderBy);

            _query.ApplySkip(sql);
            _query.ApplyTake(limit, sql);
        }

        private string determineOrderClause(ChildDocument document)
        {
            var orders = bodyClauses()
                .SelectMany<IBodyClause, (Ordering Clause, bool CaseSensitive)>(x =>
                {
                    switch (x)
                    {
                        case OrderByClause orderByClause:
                            return orderByClause.Orderings.Select(o => (o, true));
                        case OrderByComparerClause orderByComparerClause:
                            return orderByComparerClause.Orderings.Select(o => (o, orderByComparerClause.CaseSensitive));
                        default:
                            return Enumerable.Empty<(Ordering, bool)>();
                    }
                })
                .ToArray();

            if (!orders.Any())
                return string.Empty;

            return " order by "
                   + orders.Select(x => toOrderClause(document, x.Clause, x.CaseSensitive)).Join(", ");
        }

        private static string toOrderClause(ChildDocument document, Ordering clause, bool caseSensitive)
        {
            var locator = document.JsonLocator(clause.Expression);
            if (!caseSensitive)
            {
                locator = "lower(" + locator + ")";
            }
            return clause.OrderingDirection == OrderingDirection.Asc
                ? locator
                : locator + " desc";
        }

        private IWhereFragment buildWhereFragment(ChildDocument document)
        {
            var wheres = findOperators<WhereClause>().ToArray();
            if (!wheres.Any())
                return null;

            return wheres.Length == 1
                ? _store.Parser.ParseWhereFragment(document, wheres.Single().Predicate)
                : new CompoundWhereFragment(_store.Parser, document, "and", wheres);
        }
    }
}
