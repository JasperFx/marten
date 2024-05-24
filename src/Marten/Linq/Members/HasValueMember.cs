using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Parsing;
using Marten.Linq.Parsing.Operators;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class HasValueMember: IQueryableMember, IComparableMember, IBooleanMember
{
    private readonly string _isNotNullSql;
    private readonly string _isNullSql;

    public HasValueMember(IQueryableMember inner)
    {
        Inner = inner;

        TypedLocator = inner.TypedLocator;
        RawLocator = inner.RawLocator;
        MemberType = inner.MemberType;
        JSONBLocator = inner.JSONBLocator;

        _isNullSql = $"{RawLocator} is null";
        _isNotNullSql = $"{RawLocator} is not null";

        Ancestors = inner.Ancestors;
    }

    public IQueryableMember Inner { get; }

    public ISqlFragment BuildIsTrueFragment()
    {
        return new IsNotNullFilter(Inner);
    }

    public ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        var isTrue = (bool)constant.Value();

        if (op == "==" || op == "=")
        {
            return isTrue ? new IsNotNullFilter(Inner) : new IsNullFilter(Inner);
        }

        if (op == "!=")
        {
            return isTrue ? new IsNullFilter(Inner) : new IsNotNullFilter(Inner);
        }

        throw new BadLinqExpressionException(
            $"Marten does not support the '{op}' operator for Nullable HasValue members");
    }

    public string NullTestLocator => RawLocator;

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_isNotNullSql);
    }

    public string JsonPathSegment => "";
    public string MemberName => "HasValue";

    public Type MemberType { get; }
    public string TypedLocator { get; }
    public string RawLocator { get; }
    public string JSONBLocator { get; }

    public string BuildOrderingExpression(Ordering ordering, CasingRule casingRule)
    {
        return _isNotNullSql;
    }

    public IQueryableMember[] Ancestors { get; }

    public Dictionary<string, object> FindOrPlaceChildDictionaryForContainment(Dictionary<string, object> dict)
    {
        throw new NotSupportedException();
    }

    public void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict, ConstantExpression constant)
    {
        throw new NotSupportedException();
    }

    string IQueryableMember.LocatorForIncludedDocumentId => throw new NotSupportedException();

    public string SelectorForDuplication(string pgType)
    {
        throw new NotSupportedException();
    }
}
