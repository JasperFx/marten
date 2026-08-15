#nullable enable
using Marten.Linq.Members;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

public class BooleanFieldIsTrue: IReversibleWhereFragment
{
    public BooleanFieldIsTrue(IQueryableMember member)
    {
        Member = member;
    }

    /// <summary>
    ///     The boolean member being tested. Exposed for #5223, where a bare boolean predicate inside a
    ///     child collection's <c>Count()</c> has to be rewritten as an equivalent member comparison to
    ///     reach the jsonpath tier.
    /// </summary>
    public IQueryableMember Member { get; }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("(");
        builder.Append(Member.RawLocator);
        builder.Append(" is not null and ");
        builder.Append(Member.TypedLocator);
        builder.Append(" = True)");
    }

    public ISqlFragment Reverse()
    {
        return new BooleanFieldIsFalse(Member);
    }
}
