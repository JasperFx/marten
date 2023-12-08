using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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

internal class DictionaryValuesMember : QueryableMember, ICollectionMember, IValueCollectionMember
{
    private readonly IDictionaryMember _parent;

    public DictionaryValuesMember(IDictionaryMember parent, StoreOptions options) : base(parent, "Values", typeof(ICollection<>).MakeGenericType(parent.ValueType))
    {
        _parent = parent;
        ElementType = parent.ValueType;

        var rawLocator = RawLocator;

        SelectManyUsage = new SelectManyValueCollection(ElementType, _parent, options);

        var innerPgType = PostgresqlProvider.Instance.GetDatabaseType(ElementType, EnumStorage.AsInteger);
        var pgType = PostgresqlProvider.Instance.HasTypeMapping(ElementType) ? innerPgType + "[]" : "jsonb";
        Element = new SimpleElementMember(ElementType, pgType);

        ExplodeLocator = $"jsonb_path_query({_parent.TypedLocator}, '$.*') ->> 0";

        ArrayLocator = $"CAST(ARRAY(SELECT jsonb_array_elements_text(CAST({rawLocator} as jsonb))) as {innerPgType}[])";

    }

    public Type ElementType { get; }

    public SelectManyValueCollection SelectManyUsage { get;}
    public string ExplodeLocator { get; }
    public string ArrayLocator { get; }

    public override IQueryableMember FindMember(MemberInfo member)
    {
        if (member.Name == "Count" || member.Name == "Length")
        {
            return _parent.Count;
        }

        return Element;
    }

    public Statement BuildSelectManyStatement(CollectionUsage collectionUsage, IMartenSession session,
        SelectorStatement parentStatement, QueryStatistics statistics)
    {
        var statement = ElementType == typeof(string)
            ? new ScalarSelectManyStringStatement(parentStatement)
            : typeof(ScalarSelectManyStatement<>).CloseAndBuildAs<SelectorStatement>(parentStatement,
                session.Serializer, ElementType);

        // If the collection has any Where() or OrderBy() usages, you'll need an extra statement
        if (collectionUsage.OrderingExpressions.Any() || collectionUsage.WhereExpressions.Any())
        {
            statement.ConvertToCommonTableExpression(session);

            var selectorStatement = new SelectorStatement { SelectClause = statement.SelectClause.As<IScalarSelectClause>().CloneToOtherTable(statement.ExportName) };
            statement.AddToEnd(selectorStatement);

            collectionUsage.ConfigureStatement(session, SelectManyUsage, selectorStatement, statistics);
            return selectorStatement;
        }

        collectionUsage.ConfigureStatement(session, SelectManyUsage, statement, statistics);

        return statement;
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
        throw new NotImplementedException();
    }

    public ISqlFragment ParseWhereForAll(MethodCallExpression body, IReadOnlyStoreOptions options)
    {
        throw new BadLinqExpressionException(
            "Marten does not (yet) support All() operators against Dictionary pair collections");
    }

    public ISqlFragment ParseWhereForContains(MethodCallExpression body, IReadOnlyStoreOptions options)
    {
        if (body.Object is ParameterExpression)
        {
            var constant = body.Arguments.First().ReduceToConstant();


            return new DictionaryValuesContainFilter(_parent, options.Serializer(), constant);
        }

        if (body.Arguments.First().TryToParseConstant(out var constant2))
        {
            return new DictionaryValuesContainFilter(_parent, options.Serializer(), constant2);
        }

        throw new BadLinqExpressionException($"Marten does not (yet) support Contains() parsing for '{body}'");
    }

    public ISqlFragment IsEmpty => _parent.IsEmpty;
    public ISqlFragment NotEmpty => _parent.NotEmpty;
    public IEnumerator<IQueryableMember> GetEnumerator()
    {
        throw new NotSupportedException();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IQueryableMember Element { get; }
}
