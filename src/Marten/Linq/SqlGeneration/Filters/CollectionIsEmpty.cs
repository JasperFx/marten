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
        // deeply ashamed by the replace here, but it does work.
        var jsonPath = member.WriteJsonPath().Replace("[*]", "");
        _text = $"d.data @? '$ ? (@.{jsonPath} == null || @.{jsonPath}.size() == 0)'";
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
