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
        _text = $"d.data @? '$ ? (@.{jsonPath} == null || @.{jsonPath}.size() == 0)'";
    }

    public ISqlFragment Reverse()
    {
        return new CollectionIsNotEmpty(_member);
    }

    public void Apply(CommandBuilder builder)
    {
        builder.Append(_text);
    }

    public bool Contains(string sqlText)
    {
        return false;
    }
}
