#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members.Dictionaries;

internal class DictionaryKeysMember: QueryableMember, ICollectionMember, IValueCollectionMember
{
    private readonly IDictionaryMember _parent;

    public DictionaryKeysMember(IDictionaryMember parent, StoreOptions options) : base(parent, "Keys", typeof(ICollection<>).MakeGenericType(parent.KeyType))
    {
        _parent = parent;
        ElementType = parent.ValueType;

        var rawLocator = $"jsonb_path_query_array({parent.TypedLocator}, '$.keyvalue().key')";
        var innerPgType = PostgresqlProvider.Instance.GetDatabaseType(ElementType, EnumStorage.AsInteger);
        var pgType = PostgresqlProvider.Instance.HasTypeMapping(ElementType) ? innerPgType + "[]" : "jsonb";

        ArrayLocator = $"CAST(ARRAY(SELECT jsonb_array_elements_text(CAST({rawLocator} as jsonb))) as {innerPgType}[])";

        LocatorForIncludedDocumentId =
            $"UNNEST({ArrayLocator})";

        ExplodeLocator = LocatorForIncludedDocumentId;

        SelectManyUsage = new SelectManyValueCollection(ElementType, parent, options);

        Element = new SimpleElementMember(ElementType, pgType);
    }

    public SelectManyValueCollection SelectManyUsage { get;}

    public Type ElementType { get; }
    public string ExplodeLocator { get; }
    public string ArrayLocator { get; }

    public override string LocatorForIncludedDocumentId { get; }

    public Statement AttachSelectManyStatement(CollectionUsage collectionUsage, IMartenSession session,
        SelectorStatement parentStatement, QueryStatistics statistics)
    {
        var statement = ElementType == typeof(string)
            ? new ScalarSelectManyStringStatement(parentStatement)
            : typeof(ScalarSelectManyStatement<>).CloseAndBuildAs<SelectorStatement>(parentStatement,
                session.Serializer, ElementType);

        parentStatement.AddToEnd(statement);

        // If the collection has any Where() or OrderBy() usages, you'll need an extra statement
        if (collectionUsage.OrderingExpressions.Any() || collectionUsage.WhereExpressions.Any())
        {
            statement.ConvertToCommonTableExpression(session);

            var selectorStatement = new SelectorStatement { SelectClause = statement.SelectClause.As<IScalarSelectClause>().CloneToOtherTable(statement.ExportName) };
            statement.AddToEnd(selectorStatement);

            return collectionUsage.ConfigureSelectManyStatement(session, SelectManyUsage, selectorStatement, statistics).SelectorStatement();
        }

        return collectionUsage.ConfigureSelectManyStatement(session, SelectManyUsage, statement, statistics).SelectorStatement();

    }

    public ISelectClause BuildSelectClauseForExplosion(string fromObject)
    {
        return typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(fromObject, ExplodeLocator,
            ElementType);
    }

    public ISqlFragment ParseWhereForAny(Expression body, IReadOnlyStoreOptions options)
    {
        var whereClause = new ChildCollectionWhereClause();
        var parser = new WhereClauseParser((StoreOptions)options, this, whereClause);
        parser.Visit(body);

        return whereClause.CompileFragment(this, options.Serializer());
    }

    public IComparableMember ParseComparableForCount(Expression body)
    {
        return _parent.Count;
    }

    public ISqlFragment ParseWhereForAll(MethodCallExpression body, IReadOnlyStoreOptions options)
    {
        throw new BadLinqExpressionException("Marten does not (yet) support Dictionary.Keys.All() in LINQ queries");
    }

    public ISqlFragment ParseWhereForContains(MethodCallExpression body, IReadOnlyStoreOptions options)
    {
        var value = body.Arguments.Last().ReduceToConstant();
        return new DictionaryContainsKeyFilter(_parent, options.Serializer(), value);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IEnumerator<IQueryableMember> GetEnumerator()
    {
        throw new NotSupportedException();
    }

    public IQueryableMember Element { get; }

    public ISqlFragment IsEmpty => _parent.IsEmpty;
    public ISqlFragment NotEmpty => _parent.NotEmpty;
}
