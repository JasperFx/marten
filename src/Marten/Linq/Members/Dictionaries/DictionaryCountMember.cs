#nullable enable
using System.Linq.Expressions;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members.Dictionaries;

internal class DictionaryCountMember: QueryableMember, IComparableMember
{
    public DictionaryCountMember(IDictionaryMember parent): base(parent, "Count", typeof(int))
    {
        RawLocator = TypedLocator = $"jsonb_array_length(jsonb_path_query_array({parent.TypedLocator}, '$.keyvalue()'))";
        Parent = parent;
    }

    public ICollectionMember Parent { get; }

    public override ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        var def = new CommandParameter(constant);
        return new ComparisonFilter(this, def, op);
    }
}
