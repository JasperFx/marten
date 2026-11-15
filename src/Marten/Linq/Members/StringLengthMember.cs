#nullable enable
using System.Linq.Expressions;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

/// <summary>
/// #5941: <c>Length</c> of a string, as PostgreSQL's <c>length()</c>. It used to fall through to the
/// default member lookup and locate a JSON field called <c>Length</c> inside the string, which PostgreSQL
/// refuses with 42883.
/// <para>
/// PostgreSQL counts characters where .NET counts UTF-16 code units, so the two disagree for a string holding a
/// character outside the Basic Multilingual Plane, such as an emoji. That is the same caveat the existing
/// <c>Select(x => x.Name.Length)</c> translation after <c>SelectMany()</c> carries. The parent is a string
/// member, either in the JSON body or duplicated into its own column.
/// </para>
/// </summary>
internal class StringLengthMember: QueryableMember, IComparableMember
{
    public StringLengthMember(IQueryableMember parent): base(parent, nameof(string.Length), typeof(int))
    {
        RawLocator = TypedLocator = $"length({parent.RawLocator})";
    }

    public override ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        return new ComparisonFilter(this, new CommandParameter(constant), op);
    }
}
