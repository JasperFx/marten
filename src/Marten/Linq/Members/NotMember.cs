using System.Linq.Expressions;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class NotMember: IComparableMember
{
    public NotMember(IComparableMember inner)
    {
        Inner = inner;
    }

    public IComparableMember Inner { get; }

    public ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        var opposite = ComparisonFilter.NotOperators[op];
        return Inner.CreateComparison(opposite, constant);
    }
}
