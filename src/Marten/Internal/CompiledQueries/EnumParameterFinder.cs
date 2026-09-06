#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Internal.CompiledQueries;

[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal class EnumParameterFinder: IParameterFinder
{
    public bool Matches(Type memberType)
    {
        return memberType.IsEnum;
    }

    public bool AreValuesUnique(object query, CompiledQueryPlan plan)
    {
        var groups = plan.QueryMembers.Where(x => x.Type.IsEnum)
            .GroupBy(x => x.Type).Where(x => x.Count() > 1).ToArray();

        if (groups.Length == 0)
        {
            return true;
        }

        foreach (var grouping in groups)
        {
            var distinctValueCount = grouping.Select(x => x.GetValueAsObject(query)).Distinct().Count();
            if (distinctValueCount != grouping.Count())
            {
                return false;
            }
        }

        return true;
    }

    public Queue<object> UniqueValueQueue(Type type)
    {
        var enumValues = type.GetEnumValues();
        return new Queue<object>(enumValues.OfType<object>());
    }

    /// <summary>
    ///     Enum members are the one shape this finder cannot close statically — the enum type comes
    ///     from the consumer's compiled query. Returning null routes <c>CompiledQueryPlan</c> to its
    ///     reflective fallback, which is correct anywhere a JIT is available; under Native AOT an
    ///     enum-valued compiled query member still needs the consumer to keep that instantiation
    ///     reachable.
    /// </summary>
    public IQueryMember? BuildQueryMember(MemberInfo member, Type memberType)
    {
        return null;
    }
}
