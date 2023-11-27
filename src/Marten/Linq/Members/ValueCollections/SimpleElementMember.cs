using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Parsing;
using Marten.Linq.Parsing.Operators;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members.ValueCollections;

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