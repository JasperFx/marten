#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Linq.Parsing.Operators;
using Weasel.Postgresql;

namespace Marten.Linq.Members;

internal class WholeDataMember: IQueryableMember
{
    public WholeDataMember(Type sourceType)
    {
        MemberType = sourceType;
    }

    public IQueryableMember[] Ancestors { get; } = Array.Empty<IQueryableMember>();

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("data");
    }

    public string MemberName => string.Empty;

    public string JsonPathSegment => string.Empty;
    public string NullTestLocator => RawLocator;

    public string SelectorForDuplication(string pgType)
    {
        throw new NotSupportedException();
    }

    public Type MemberType { get; }
    public string TypedLocator => "data";
    public string RawLocator => "data";
    public string JSONBLocator { get; set; } = "data";

    public string BuildOrderingExpression(Ordering ordering, CasingRule casingRule)
    {
        return "data";
    }

    Dictionary<string, object> IQueryableMember.FindOrPlaceChildDictionaryForContainment(
        Dictionary<string, object> dict)
    {
        throw new NotSupportedException();
    }

    void IQueryableMember.PlaceValueInDictionaryForContainment(Dictionary<string, object> dict,
        ConstantExpression constant)
    {
        throw new NotSupportedException();
    }

    string IQueryableMember.LocatorForIncludedDocumentId => throw new NotSupportedException();
}

internal class RootMember: IQueryableMember
{
    public RootMember(Type sourceType)
    {
        MemberType = sourceType;
    }

    public IQueryableMember[] Ancestors { get; set; } = Array.Empty<IQueryableMember>();

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("d.data");
    }

    public string SelectorForDuplication(string pgType)
    {
        throw new NotSupportedException();
    }

    public string NullTestLocator => RawLocator;

    public string JsonPathSegment => string.Empty;
    public string MemberName => string.Empty;
    public Type MemberType { get; }
    public string TypedLocator => "d.data";
    public string RawLocator => "d.data";
    public string JSONBLocator { get; set; } = "d.data";

    public string BuildOrderingExpression(Ordering ordering, CasingRule casingRule)
    {
        return "d.data";
    }

    Dictionary<string, object> IQueryableMember.FindOrPlaceChildDictionaryForContainment(
        Dictionary<string, object> dict)
    {
        return dict;
    }

    void IQueryableMember.PlaceValueInDictionaryForContainment(Dictionary<string, object> dict,
        ConstantExpression constant)
    {
        throw new NotSupportedException();
    }

    string IQueryableMember.LocatorForIncludedDocumentId => throw new NotSupportedException();
}
