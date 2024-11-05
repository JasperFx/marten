#nullable enable
using System.Linq.Expressions;
using Marten.Linq.Members;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

internal class AllValuesEqualFilter: ISqlFragment
{
    private readonly ConstantExpression _constant;
    private readonly ICollectionMember _member;

    public AllValuesEqualFilter(ConstantExpression constant, ICollectionMember member)
    {
        _constant = constant;
        _member = member;
    }

    public void Apply(IPostgresqlCommandBuilder builder)
    {
        builder.AppendParameter(_constant.Value!);
        builder.Append(" = ALL(");
        builder.Append(_member.ArrayLocator);
        builder.Append(")");
    }

}
