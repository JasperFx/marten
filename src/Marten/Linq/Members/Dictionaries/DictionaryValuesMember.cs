using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
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
    private readonly StoreOptions _options;
    private ImHashMap<string, IQueryableMember> _members = ImHashMap<string, IQueryableMember>.Empty;
    private readonly RootMember _root;


    public DictionaryValuesMember(IDictionaryMember parent, StoreOptions options) : base(parent, "Values", typeof(ICollection<>).MakeGenericType(parent.ValueType))
    {
        _parent = parent;
        _options = options;

        ElementType = parent.ValueType;
        _root = new RootMember(ElementType) { Ancestors = Array.Empty<IQueryableMember>() };

        var rawLocator = RawLocator;

        SelectManyUsage = new SelectManyValueCollection(ElementType, _parent, options);

        var innerPgType = PostgresqlProvider.Instance.GetDatabaseType(ElementType, EnumStorage.AsInteger);
        var pgType = PostgresqlProvider.Instance.HasTypeMapping(ElementType) ? innerPgType + "[]" : "jsonb";
        Element = new SimpleElementMember(ElementType, pgType);

        if (ElementType.IsSimple() || (ElementType.IsNullable() && ElementType.GenericTypeArguments[0].IsSimple()))
        {
            ExplodeLocator = $"jsonb_path_query({_parent.TypedLocator}, '$.*') ->> 0";
        }
        else
        {
            ExplodeLocator = $"jsonb_path_query({_parent.TypedLocator}, '$.*')";
        }

        ArrayLocator = $"CAST(ARRAY(SELECT jsonb_array_elements_text(CAST({rawLocator} as jsonb))) as {innerPgType}[])";

    }

    public override void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict, ConstantExpression constant)
    {
        base.PlaceValueInDictionaryForContainment(dict, constant);
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

        if (member is ElementMember) return Element;

        if (_members.TryFind(member.Name, out var m))
        {
            return m;
        }

        m = _options.CreateQueryableMember(member, _root);
        _members = _members.AddOrUpdate(member.Name, m);

        return m;
    }

    private SelectorStatement createSelectManySelectorStatement(IMartenSession session,
        SelectorStatement parentStatement, CollectionUsage collectionUsage, QueryStatistics statistics)
    {
        if (ElementType == typeof(string)) return new ScalarSelectManyStringStatement(parentStatement);
        if (ElementType.IsPrimitive() || (ElementType.IsNullable() && ElementType.GenericTypeArguments[0].IsPrimitive))
        {
            return typeof(ScalarSelectManyStatement<>).CloseAndBuildAs<SelectorStatement>(parentStatement,
                session.Serializer, ElementType);
        }

        var selectClause =
            typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(parentStatement.ExportName, ElementType);
        return (SelectorStatement)collectionUsage.BuildSelectManyStatement(session, this, selectClause, statistics, parentStatement);
    }

    public Statement AttachSelectManyStatement(CollectionUsage collectionUsage, IMartenSession session,
        SelectorStatement parentStatement, QueryStatistics statistics)
    {
        var statement = createSelectManySelectorStatement(session, parentStatement, collectionUsage, statistics);

        // If the collection has any Where() or OrderBy() usages, you'll need an extra statement
        if (collectionUsage.OrderingExpressions.Any() || collectionUsage.WhereExpressions.Any())
        {
            parentStatement.AddToEnd(statement);
            statement.ConvertToCommonTableExpression(session);

            var selectorStatement = new SelectorStatement { SelectClause = statement.SelectClause.As<IScalarSelectClause>().CloneToOtherTable(statement.ExportName) };
            statement.AddToEnd(selectorStatement);

            return collectionUsage.ConfigureSelectManyStatement(session, SelectManyUsage, selectorStatement, statistics).SelectorStatement();
        }

        parentStatement.AddToEnd(statement);

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
        throw new BadLinqExpressionException(
            "Marten does not (yet) support Comparing the Count against Dictionary pair collections");
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
