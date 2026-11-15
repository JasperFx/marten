#nullable enable
using System.Reflection;
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.Parsing.Operators;
using Marten.Util;

namespace Marten.Linq.Members;

public class StringMember: QueryableMember, IComparableMember
{
    private string _lowerLocator;

    internal static StringMember ForArrayIndex(ValueCollectionMember parent, ArrayIndexMember member)
    {
        return ForIndexedElement(parent, parent.RawLocator, member);
    }

    /// <summary>
    ///     #5949: the string element at <paramref name="member" /> of the jsonb array at
    ///     <paramref name="jsonbLocator" />, which may itself be an element of another array (<c>x.Words[1][0]</c>).
    /// </summary>
    internal static StringMember ForIndexedElement(IQueryableMember parent, string jsonbLocator, ArrayIndexMember member)
    {
        var locator = $"{jsonbLocator} ->> {member.Index}";
        return new StringMember(parent, Casing.Default, member)
        {
            RawLocator = locator,
            TypedLocator = locator,

            // The constructor built this from the placeholder locator, before the one above replaced it
            _lowerLocator = $"lower({locator})"
        };
    }

    public StringMember(IQueryableMember parent, Casing casing, MemberInfo member): base(parent, casing, member)
    {
        TypedLocator = RawLocator;
        _lowerLocator = $"lower({RawLocator})";
        MemberType = typeof(string);
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
            nameof(string.ToLowerInvariant) => new StringMember(this, Casing.Default, member)
            {
                RawLocator = $"lower({RawLocator})", TypedLocator = $"lower({RawLocator})"
            },
            nameof(string.ToUpperInvariant) => new StringMember(this, Casing.Default, member)
            {
                RawLocator = $"upper({RawLocator})", TypedLocator = $"upper({RawLocator})"
            },
            nameof(string.Trim) => new StringMember(this, Casing.Default, member)
            {
                RawLocator = $"trim({RawLocator})", TypedLocator = $"trim({RawLocator})"
            },
            // #5941
            nameof(string.Length) when member is PropertyInfo => new StringLengthMember(this),
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
        return RawLocator.RemoveTableAlias("d");
    }
}
