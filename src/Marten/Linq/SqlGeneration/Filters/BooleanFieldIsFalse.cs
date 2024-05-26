#nullable enable
using Marten.Linq.Members;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

public class BooleanFieldIsFalse: IReversibleWhereFragment
{
    private readonly IQueryableMember _member;

    public BooleanFieldIsFalse(IQueryableMember member)
    {
        _member = member;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("(");
        builder.Append(_member.RawLocator);
        builder.Append(" is null or ");
        builder.Append(_member.TypedLocator);
        builder.Append(" = False)");
    }

    public ISqlFragment Reverse()
    {
        return new BooleanFieldIsTrue(_member);
    }
}
