#nullable enable
using System.Linq.Expressions;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class CollectionLengthMember: QueryableMember, IComparableMember
{
    public CollectionLengthMember(ICollectionMember parent): base(parent, "Count", typeof(int))
    {
        RawLocator = TypedLocator = $"jsonb_array_length({parent.JSONBLocator})";
        Parent = parent;
    }

    public ICollectionMember Parent { get; }

    public override ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        var def = new CommandParameter(constant);
        return new ComparisonFilter(this, def, op);
    }
}
