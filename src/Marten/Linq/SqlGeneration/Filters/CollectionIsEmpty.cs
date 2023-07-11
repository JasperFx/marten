using Marten.Linq.Members;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

internal class CollectionIsEmpty: ISqlFragment, IReversibleWhereFragment
{
    private readonly ICollectionMember _member;

    public CollectionIsEmpty(ICollectionMember member)
    {
        _member = member;
    }

    public ISqlFragment Reverse()
    {
        return new CollectionIsNotEmpty(_member);
    }

    public void Apply(CommandBuilder builder)
    {
        builder.Append("(");
        builder.Append(_member.JSONBLocator);
        builder.Append(" is null or jsonb_array_length(");
        builder.Append(_member.JSONBLocator);
        builder.Append(") = 0)");
    }

    public bool Contains(string sqlText)
    {
        return false;
    }
}
