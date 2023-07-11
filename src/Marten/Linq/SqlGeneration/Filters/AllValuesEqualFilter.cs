using System.Linq.Expressions;
using Marten.Linq.Members;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

internal class AllValuesEqualFilter: ISqlFragment
{
    private readonly ConstantExpression _constant;
    private readonly ValueCollectionMember _member;

    public AllValuesEqualFilter(ConstantExpression constant, ValueCollectionMember member)
    {
        _constant = constant;
        _member = member;
    }

    public void Apply(CommandBuilder builder)
    {
        builder.AppendParameter(_constant.Value);
        builder.Append(" = ALL(");
        builder.Append(_member.ArrayLocator);
        builder.Append(")");
    }

    public bool Contains(string sqlText)
    {
        return false;
    }
}
