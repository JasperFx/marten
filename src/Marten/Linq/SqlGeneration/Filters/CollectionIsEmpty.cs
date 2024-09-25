#nullable enable
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

// TODO -- make it ICollectionAware
internal class CollectionIsEmpty: IReversibleWhereFragment
{
    private readonly ICollectionMember _member;
    private readonly string _text;

    public CollectionIsEmpty(ICollectionMember member)
    {
        _member = member;
        var jsonPath = member.WriteJsonPath();
        _text = $"d.data @? '$ ? (@.{member.MemberName} == null || @.{member.MemberName}.size() == 0)'";
    }

    public ISqlFragment Reverse()
    {
        return _member.NotEmpty;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_text);
    }
}
