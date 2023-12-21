using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members.ValueCollections;

internal class ValueCollectionMember: QueryableMember, ICollectionMember, IValueCollectionMember, ISelectableMember
{
    private readonly IQueryableMember _count;
    private readonly WholeDataMember _wholeDataMember;

    public ValueCollectionMember(StoreOptions storeOptions, IQueryableMember parent, Casing casing, MemberInfo member): base(parent, casing,
        member)
    {
        if (member is ElementMember element)
        {
            MemberType = element.ReflectedType!;
        }

        ElementType = MemberType.DetermineElementType();
        var rawLocator = RawLocator;
        var innerPgType = PostgresqlProvider.Instance.GetDatabaseType(ElementType, EnumStorage.AsInteger);
        var pgType = PostgresqlProvider.Instance.HasTypeMapping(ElementType) ? innerPgType + "[]" : "jsonb";

        SimpleLocator = $"{parent.RawLocator} -> '{MemberName}'";

        RawLocator = $"CAST({rawLocator} as jsonb)";
        TypedLocator = $"CAST({rawLocator} as {pgType})";

        _count = new CollectionLengthMember(this);


        ArrayLocator = $"CAST(ARRAY(SELECT jsonb_array_elements_text(CAST({rawLocator} as jsonb))) as {innerPgType}[])";


        LocatorForIncludedDocumentId =
            $"UNNEST({ArrayLocator})";

        ExplodeLocator = LocatorForIncludedDocumentId;

        _wholeDataMember = new WholeDataMember(ElementType);

        Element = new SimpleElementMember(ElementType, pgType);

        SelectManyUsage = new SelectManyValueCollection(this, member, ElementType, storeOptions);

        IsEmpty = new CollectionIsEmpty(this);
        NotEmpty = new CollectionIsNotEmpty(this);
    }

    /// <summary>
    /// Only used to craft children locators later
    /// </summary>
    public string SimpleLocator { get; }

    public ISqlFragment IsEmpty { get; }
    public ISqlFragment NotEmpty { get; }

    public SelectManyValueCollection SelectManyUsage { get;}

    public IQueryableMember Element { get; }

    public string ArrayLocator { get; set; }

    public IComparableMember ParseComparableForCount(Expression body)
    {
        throw new BadLinqExpressionException(
            "Marten does not (yet) support Linq filters within the Count() expression of a scalar value collection");
    }

    public ISqlFragment ParseWhereForAll(MethodCallExpression expression, IReadOnlyStoreOptions options)
    {
        var constant = expression.Arguments[1].ReduceToConstant();

        if (constant.Value() == null) return new AllValuesAreNullFilter(this);

        return new AllValuesEqualFilter(constant, this);
    }

    public ISqlFragment ParseWhereForContains(MethodCallExpression body, IReadOnlyStoreOptions options)
    {
        var value = body.Arguments.Last().ReduceToConstant();

        return new ContainmentWhereFilter(this, value, options.Serializer());
    }

    public string LocatorForIncludedDocumentId { get; }

    public Type ElementType { get; }

    public string ExplodeLocator { get; }

    public ISqlFragment ParseWhereForAny(Expression body, IReadOnlyStoreOptions options)
    {
        var whereClause = new ChildCollectionWhereClause();
        var parser = new WhereClauseParser((StoreOptions)options, this, whereClause);
        parser.Visit(body);

        return whereClause.CompileFragment(this, options.Serializer());
    }

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


        var final = collectionUsage.ConfigureSelectManyStatement(session, SelectManyUsage, statement, statistics)
            .SelectorStatement();

        return final;
    }

    public ISelectClause BuildSelectClauseForExplosion(string fromObject)
    {
        var selection = $"jsonb_array_elements_text({JSONBLocator})";

        return typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(fromObject, selection,
            ElementType);
    }

    public override void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict,
        ConstantExpression constant)
    {
        dict[MemberName] = new[] { constant.Value };
    }

    public override Dictionary<string, object> FindOrPlaceChildDictionaryForContainment(Dictionary<string, object> dict)
    {
        throw new NotSupportedException();
    }

    public override string SelectorForDuplication(string pgType)
    {
        if (pgType.EqualsIgnoreCase("JSONB"))
        {
            return JSONBLocator.Replace("d.", "");
        }

        return $"CAST(ARRAY(SELECT jsonb_array_elements_text({RawLocator.Replace("d.", "")})) as {pgType})";
    }

    public override IQueryableMember FindMember(MemberInfo member)
    {
        if (member.Name == "Count" || member.Name == "Length")
        {
            return _count;
        }

        // TODO -- this could be memoized a bit
        if (member is ArrayIndexMember indexMember)
            return ElementType == typeof(string)
                ? StringMember.ForArrayIndex(this, indexMember)
                : SimpleCastMember.ForArrayIndex(this, indexMember);

        return _wholeDataMember;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IEnumerator<IQueryableMember> GetEnumerator()
    {
        throw new NotSupportedException();
    }

    public void Apply(CommandBuilder builder, ISerializer serializer)
    {
        builder.Append(RawLocator);
    }
}
