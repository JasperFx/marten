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
using Marten.Linq.Parsing.Operators;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class ValueCollectionMember: QueryableMember, ICollectionMember, IQueryableMemberCollection
{
    private readonly IQueryableMember _count;
    private readonly WholeDataMember _wholeDataMember;

    public ValueCollectionMember(IQueryableMember parent, Casing casing, MemberInfo member): base(parent, casing,
        member)
    {
        ElementType = MemberType.DetermineElementType();
        var rawLocator = RawLocator;
        var innerPgType = PostgresqlProvider.Instance.GetDatabaseType(ElementType, EnumStorage.AsInteger);
        var pgType = PostgresqlProvider.Instance.HasTypeMapping(ElementType) ? innerPgType + "[]" : "jsonb";

        RawLocator = $"CAST({rawLocator} as jsonb)";
        TypedLocator = $"CAST({rawLocator} as {pgType})";

        _count = new CollectionLengthMember(this);


        ArrayLocator = $"CAST(ARRAY(SELECT jsonb_array_elements_text(CAST({rawLocator} as jsonb))) as {innerPgType}[])";


        LocatorForIncludedDocumentId =
            $"UNNEST({ArrayLocator})";

        ExplodeLocator = LocatorForIncludedDocumentId;

        _wholeDataMember = new WholeDataMember(ElementType);

        ElementMember = new SimpleElementMember(ElementType, pgType);
    }

    public IQueryableMember ElementMember { get; }

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

    public SelectorStatement BuildChildStatement(CollectionUsage collectionUsage, IMartenSession session,
        SelectorStatement parentStatement)
    {
        var statement = ElementType == typeof(string)
            ? new ScalarSelectManyStringStatement(parentStatement)
            : typeof(ScalarSelectManyStatement<>).CloseAndBuildAs<SelectorStatement>(parentStatement,
                session.Serializer, ElementType);

        collectionUsage.ProcessSingleValueModeIfAny(statement);

        return statement;
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
}

internal class SimpleElementMember: IQueryableMember, IComparableMember
{
    public SimpleElementMember(Type memberType, string pgType)
    {
        MemberType = memberType;
        PgType = pgType;
        TypedLocator = $"CAST(data as {pgType})";
        MemberName = "Element";
    }

    public string PgType { get; }

    public ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (constant.Value == null)
        {
            throw new BadLinqExpressionException("Marten cannot search for null values in collections");
        }

        return new ElementComparisonFilter(constant.Value(), op);
    }

    public string MemberName { get; }

    public Type MemberType { get; }

    public void Apply(CommandBuilder builder)
    {
        builder.Append("data");
    }

    public bool Contains(string sqlText)
    {
        return false;
    }

    public string NullTestLocator => RawLocator;
    public string JsonPathSegment { get; } = "data";
    public string TypedLocator { get; }
    public string RawLocator { get; } = "data";
    public string JSONBLocator { get; } = "data";

    public string BuildOrderingExpression(Ordering ordering, CasingRule casingRule)
    {
        return "data";
    }

    public IQueryableMember[] Ancestors { get; } = Array.Empty<IQueryableMember>();

    public Dictionary<string, object> FindOrPlaceChildDictionaryForContainment(Dictionary<string, object> dict)
    {
        throw new NotSupportedException();
    }

    public void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict, ConstantExpression constant)
    {
        throw new NotSupportedException();
    }

    public string LocatorForIncludedDocumentId => TypedLocator;

    public virtual string SelectorForDuplication(string pgType)
    {
        return $"CAST({RawLocator.Replace("d.", "")} as {pgType})";
    }
}

internal class ElementComparisonFilter: ISqlFragment, ICollectionAware
{
    public ElementComparisonFilter(object value, string op)
    {
        Value = value;
        Op = op;
    }

    public object Value { get; }
    public string Op { get; }

    public IEnumerable<DictionaryValueUsage> Values()
    {
        yield return new DictionaryValueUsage(Value);
    }

    bool ICollectionAware.CanReduceInChildCollection()
    {
        if (Value == null) return false;
        if (Value.GetType().IsDateTime()) return false;
        if (Value.GetType() == typeof(DateTimeOffset)) return false;
        return true;
    }

    ICollectionAwareFilter ICollectionAware.BuildFragment(ICollectionMember member, ISerializer serializer)
    {
        switch (Op)
        {
            case "=":
                return ContainmentWhereFilter.ForValue(member, Value, serializer);

            case "!=":
                return (ICollectionAwareFilter)ContainmentWhereFilter.ForValue(member, Value, serializer).Reverse();

            default:
                throw new BadLinqExpressionException(
                    $"Marten does not (yet) support the {Op} operator in element member queries");
        }
    }

    bool ICollectionAware.SupportsContainment()
    {
        // Little goofy. Make it do its own thing
        return false;
    }

    void ICollectionAware.PlaceIntoContainmentFilter(ContainmentWhereFilter filter)
    {
        throw new NotSupportedException();
    }

    public bool CanBeJsonPathFilter()
    {
        return true;
    }

    public void BuildJsonPathFilter(CommandBuilder builder, Dictionary<string, object> parameters)
    {
        var parameter = parameters.AddJsonPathParameter(Value);

        builder.Append("@ ");
        builder.Append(Op);
        builder.Append(" ");
        builder.Append(parameter);
    }

    void ISqlFragment.Apply(CommandBuilder builder)
    {
        throw new NotSupportedException();
    }

    bool ISqlFragment.Contains(string sqlText)
    {
        throw new NotSupportedException();
    }
}
