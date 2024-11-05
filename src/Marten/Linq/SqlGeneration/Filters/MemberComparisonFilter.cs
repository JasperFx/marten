#nullable enable
using System.Collections.Generic;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Weasel.Core.Serialization;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

public class MemberComparisonFilter: ComparisonFilter, ICollectionAware
{
    public MemberComparisonFilter(IQueryableMember member, ISqlFragment right, string op): base(member, right, op)
    {
        Member = member;
    }

    public IQueryableMember Member { get; }

    public bool CanReduceInChildCollection()
    {
        return Op == "=" && Right is CommandParameter;
    }

    public ICollectionAwareFilter BuildFragment(ICollectionMember member, ISerializer serializer)
    {
        var fragment = new ContainmentWhereFilter(member, serializer);
        var value = Right.As<CommandParameter>().Value;
        fragment.PlaceMemberValue(Member, Expression.Constant(value));
        fragment.Usage = ContainmentUsage.Collection;

        return fragment;
    }

    public bool SupportsContainment()
    {
        return Op == "=" && Right is CommandParameter;
    }

    public void PlaceIntoContainmentFilter(ContainmentWhereFilter filter)
    {
        filter.PlaceMemberValue(Member, Expression.Constant(Right.As<CommandParameter>().Value));
    }

    public bool CanBeJsonPathFilter()
    {
        return Right is CommandParameter;
    }

    public void BuildJsonPathFilter(IPostgresqlCommandBuilder builder, Dictionary<string, object> parameters)
    {
        var rawValue = Right.As<CommandParameter>().Value!;
        var parameter = parameters.AddJsonPathParameter(rawValue);

        builder.Append("@.");
        Member.WriteJsonPath(builder);
        builder.Append(" ");

        builder.Append(Op.CorrectJsonPathOperator());
        builder.Append(" ");
        builder.Append(parameter);
    }

    public IEnumerable<DictionaryValueUsage> Values()
    {
        var rawValue = Right.As<CommandParameter>().Value;
        yield return new DictionaryValueUsage(rawValue);
    }
}
