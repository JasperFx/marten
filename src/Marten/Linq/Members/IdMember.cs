using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Linq.Parsing.Operators;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class IdMember: IQueryableMember, IComparableMember
{
    private const string _orderDescending = "d.id desc";

    public IdMember(MemberInfo member)
    {
        MemberType = member.GetMemberType();

        JSONBLocator = $"CAST({RawLocator} as jsonb)";
        Ancestors = Array.Empty<IQueryableMember>();

        MemberName = member.Name;
        Member = member;
    }

    public MemberInfo Member { get; }

    public ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (constant.Value == null)
        {
            return op == "=" ? new IsNullFilter(this) : new IsNotNullFilter(this);
        }

        var def = new CommandParameter(constant);
        return new ComparisonFilter(this, def, op);
    }

    public string MemberName { get; }

    public string NullTestLocator => RawLocator;

    public string SelectorForDuplication(string pgType)
    {
        throw new NotSupportedException();
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(TypedLocator);
    }

    public string JsonPathSegment => "";

    public Type MemberType { get; }
    public string TypedLocator => "d.id";
    public string RawLocator => "d.id";
    public string JSONBLocator { get; set; } = "d.id";

    public string BuildOrderingExpression(Ordering ordering, CasingRule casingRule)
    {
        if (ordering.Direction == OrderingDirection.Desc)
        {
            return _orderDescending;
        }

        return TypedLocator;
    }

    public IQueryableMember[] Ancestors { get; }

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

    string IQueryableMember.LocatorForIncludedDocumentId => throw new NotImplementedException();
}
