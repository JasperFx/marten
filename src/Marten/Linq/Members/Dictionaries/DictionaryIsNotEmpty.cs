#nullable enable
using Marten.Linq.Parsing;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members.Dictionaries;

internal class DictionaryIsNotEmpty: IReversibleWhereFragment
{
    private readonly IDictionaryMember _parent;
    private readonly string _text;

    public DictionaryIsNotEmpty(IDictionaryMember parent)
    {
        _parent = parent;
        var jsonPath = parent.WriteJsonPath();
        _text = $"({parent.TypedLocator} is not null and jsonb_array_length(jsonb_path_query_array(d.data, '$.{jsonPath}.keyvalue()')) > 0)";
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_text);
    }

    public ISqlFragment Reverse()
    {
        return _parent.IsEmpty;
    }
}
