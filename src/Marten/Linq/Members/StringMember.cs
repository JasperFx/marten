#nullable enable
using System.Reflection;
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.Parsing.Operators;
using Weasel.Core.Serialization;

namespace Marten.Linq.Members;

public class StringMember: QueryableMember, IComparableMember
{
    private readonly string _lowerLocator;

    internal static StringMember ForArrayIndex(ValueCollectionMember parent, ArrayIndexMember member)
    {
        return new StringMember(parent, Casing.Default, member)
        {
            RawLocator = $"{parent.RawLocator} ->> {member.Index}",
            TypedLocator = $"{parent.RawLocator} ->> {member.Index}"
        };
    }

    public StringMember(IQueryableMember parent, Casing casing, MemberInfo member): base(parent, casing, member)
    {
        TypedLocator = RawLocator;
        _lowerLocator = $"lower({RawLocator})";
    }

    public override IQueryableMember FindMember(MemberInfo member)
    {
        return member.Name switch
        {
            nameof(string.ToLower) => new StringMember(this, Casing.Default, member)
            {
                RawLocator = $"lower({RawLocator})", TypedLocator = $"lower({RawLocator})"
            },
            nameof(string.ToUpper) => new StringMember(this, Casing.Default, member)
            {
                RawLocator = $"upper({RawLocator})", TypedLocator = $"upper({RawLocator})"
            },
            nameof(string.Trim) => new StringMember(this, Casing.Default, member)
            {
                RawLocator = $"trim({RawLocator})", TypedLocator = $"trim({RawLocator})"
            },
            _ => base.FindMember(member)
        };
    }

    public override string BuildOrderingExpression(Ordering ordering, CasingRule casingRule)
    {
        var expression = casingRule == CasingRule.CaseSensitive ? RawLocator : _lowerLocator;

        if (ordering.Direction == OrderingDirection.Desc)
        {
            return $"{expression} desc";
        }

        return expression;
    }

    public override string SelectorForDuplication(string pgType)
    {
        return RawLocator.Replace("d.", "");
    }
}
