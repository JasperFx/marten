using System.Reflection;
using Marten.Linq.Parsing.Operators;

namespace Marten.Linq.Members;

internal class StringMember: QueryableMember, IComparableMember
{
    private readonly string _lowerLocator;

    public StringMember(IQueryableMember parent, Casing casing, MemberInfo member): base(parent, casing, member)
    {
        TypedLocator = RawLocator;
        _lowerLocator = $"lower({RawLocator})";
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
