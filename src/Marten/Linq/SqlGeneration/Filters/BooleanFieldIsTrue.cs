using Marten.Linq.Members;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

public class BooleanFieldIsTrue: IReversibleWhereFragment
{
    private readonly IQueryableMember _member;

    public BooleanFieldIsTrue(IQueryableMember member)
    {
        _member = member;
    }

    public void Apply(CommandBuilder builder)
    {
        builder.Append("(");
        builder.Append(_member.RawLocator);
        builder.Append(" is not null and ");
        builder.Append(_member.TypedLocator);
        builder.Append(" = True)");
    }

    public bool Contains(string sqlText)
    {
        return _member.RawLocator.Contains(sqlText);
    }

    public ISqlFragment Reverse()
    {
        return new BooleanFieldIsFalse(_member);
    }
}
