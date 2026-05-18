#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Marten.Linq;
using Marten.Linq.Includes;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Internal.CompiledQueries;

/// <summary>
/// Runtime descriptor builder used by <see cref="CompiledQueryCollection"/> when a
/// compiled-query type does not have a source-generator-emitted
/// <see cref="CompiledQueryHandlerDescriptor"/> already registered (i.e. the
/// consumer assembly is missing the <c>[JasperFxAssembly]</c> marker, or the
/// query type was registered at runtime via reflection). Replaces the Roslyn
/// emit in <see cref="CompiledQuerySourceBuilder"/> with reflection-driven
/// member readers + <see cref="MakeGenericMethod"/> dispatch onto the same
/// <see cref="Include.ReaderToAction{T}"/> /
/// <see cref="Include.ReaderToList{T}"/> /
/// <see cref="Include.ReaderToDictionary{T,TId}"/> factories the source
/// generator emits as typed calls.
/// </summary>
/// <remarks>
/// <para>
/// Once built, the descriptor is registered with
/// <see cref="CompiledQueryHandlerRegistry"/> so subsequent calls for the same
/// query type hit the fast source-gen-style path. Build cost is paid once per
/// query type, not per <c>session.Query(...)</c> invocation.
/// </para>
/// <para>
/// This is the "FEC fallback" referenced in #4454 Phase 1D — the reflection /
/// <see cref="MakeGenericMethod"/> path that Phase 5's AOT-compliance pass may
/// later replace with a pure-source-gen contract behind
/// <c>[RequiresDynamicCode]</c>.
/// </para>
/// </remarks>
[RequiresUnreferencedCode("Uses reflection over user-supplied compiled-query types and MakeGenericMethod for Include reader dispatch.")]
[RequiresDynamicCode("Uses MakeGenericMethod for Include reader dispatch.")]
internal static class RuntimeCompiledQueryDescriptorFactory
{
    public static CompiledQueryHandlerDescriptor Build(CompiledQueryPlan plan)
    {
        // Source-gen emits one switch case per parameter member; the runtime
        // equivalent is a dictionary lookup feeding a per-member binder
        // delegate, captured once per query type.
        var binders = new Dictionary<string, Action<NpgsqlParameter, object, bool>>(plan.QueryMembers.Count);
        foreach (var member in plan.QueryMembers)
        {
            binders[member.Member.Name] = BuildMemberBinder(member);
        }

        var parameterMemberNames = plan.QueryMembers.Select(x => x.Member.Name).ToArray();
        var includeMemberNames = plan.IncludeMembers.Select(x => x.Name).ToArray();
        var statisticsMember = plan.StatisticsMember;
        var includeMembers = plan.IncludeMembers.ToArray();

        Action<NpgsqlParameter, object, string, bool> bindParameter = (parameter, query, memberName, enumAsString) =>
        {
            if (binders.TryGetValue(memberName, out var binder))
            {
                binder(parameter, query, enumAsString);
            }
            else
            {
                throw new InvalidOperationException(
                    $"No compiled-query parameter binder registered for member '{memberName}' on " +
                    $"{plan.QueryType.FullName}. Expected one of: {string.Join(", ", binders.Keys)}.");
            }
        };

        Func<IMartenSession, object, IIncludeReader[]> attachIncludeReaders = includeMembers.Length == 0
            ? (_, _) => Array.Empty<IIncludeReader>()
            : (session, query) => BuildIncludeReaders(includeMembers, session, query);

        Func<object, QueryStatistics?> readStatistics = statisticsMember is null
            ? _ => null
            : query =>
            {
                var current = CompiledQueryMemberReader.Read(statisticsMember, query) as QueryStatistics;
                return current ?? new QueryStatistics();
            };

        var docType = ResolveDocType(plan.QueryType);

        return new CompiledQueryHandlerDescriptor(
            plan.QueryType,
            docType,
            plan.OutputType,
            parameterMemberNames,
            includeMemberNames,
            statisticsMember?.Name,
            bindParameter,
            attachIncludeReaders,
            readStatistics);
    }

    private static Type ResolveDocType(Type queryType)
    {
        // ICompiledQuery<TDoc, TOut> is the canonical contract every compiled
        // query implements. ICompiledListQuery<TDoc, ...>, ICompiledQueryStream<TDoc>
        // etc. all extend it, so AllInterfaces + match-by-generic-definition gives
        // us TDoc without a per-shape special case.
        foreach (var iface in queryType.GetInterfaces())
        {
            if (!iface.IsGenericType) continue;
            if (iface.GetGenericTypeDefinition() == typeof(ICompiledQuery<,>))
            {
                return iface.GetGenericArguments()[0];
            }
        }

        throw new InvalidOperationException(
            $"Type {queryType.FullName} does not implement ICompiledQuery<,>; cannot resolve the TDoc type for the runtime descriptor.");
    }

    private static Action<NpgsqlParameter, object, bool> BuildMemberBinder(IQueryMember member)
    {
        var memberInfo = member.Member;
        var memberType = member.Type;

        if (memberType.IsEnum)
        {
            return (parameter, query, enumAsString) =>
            {
                var value = CompiledQueryMemberReader.Read(memberInfo, query);
                if (enumAsString)
                {
                    parameter.NpgsqlDbType = NpgsqlDbType.Varchar;
                    parameter.Value = value?.ToString() ?? (object)DBNull.Value;
                }
                else
                {
                    parameter.NpgsqlDbType = NpgsqlDbType.Integer;
                    parameter.Value = value is null ? (object)DBNull.Value : Convert.ToInt32(value);
                }
            };
        }

        if (memberType == typeof(byte[]))
        {
            return (parameter, query, _) =>
            {
                parameter.NpgsqlDbType = NpgsqlDbType.Bytea;
                parameter.Value = CompiledQueryMemberReader.Read(memberInfo, query) ?? (object)DBNull.Value;
            };
        }

        if (memberType.IsArray)
        {
            var elementType = memberType.GetElementType()!;
            var compositeType = ResolveArrayNpgsqlType(elementType);
            return (parameter, query, _) =>
            {
                parameter.NpgsqlDbType = compositeType;
                parameter.Value = CompiledQueryMemberReader.Read(memberInfo, query) ?? (object)DBNull.Value;
            };
        }

        var scalarType = PostgresqlProvider.Instance.ToParameterType(memberType);
        return (parameter, query, _) =>
        {
            parameter.NpgsqlDbType = scalarType;
            parameter.Value = CompiledQueryMemberReader.Read(memberInfo, query) ?? (object)DBNull.Value;
        };
    }

    private static NpgsqlDbType ResolveArrayNpgsqlType(Type elementType)
    {
        // Mirrors ParameterUsage.npgsqlArrayDbTypeCodeFor — the codegen branch
        // emits the composite NpgsqlDbType for the common array element types
        // explicitly because PostgresqlProvider.ToParameterType doesn't
        // synthesize them at runtime. Keep the two lists in lockstep until
        // Phase 1E retires the codegen path.
        if (elementType == typeof(string)) return NpgsqlDbType.Array | NpgsqlDbType.Varchar;
        if (elementType == typeof(Guid)) return NpgsqlDbType.Array | NpgsqlDbType.Uuid;
        if (elementType == typeof(int)) return NpgsqlDbType.Array | NpgsqlDbType.Integer;
        if (elementType == typeof(long)) return NpgsqlDbType.Array | NpgsqlDbType.Bigint;
        if (elementType == typeof(float)) return NpgsqlDbType.Array | NpgsqlDbType.Real;
        if (elementType == typeof(decimal)) return NpgsqlDbType.Array | NpgsqlDbType.Numeric;
        if (elementType == typeof(DateTime)) return NpgsqlDbType.Array | NpgsqlDbType.Timestamp;
        if (elementType == typeof(DateTimeOffset)) return NpgsqlDbType.Array | NpgsqlDbType.TimestampTz;

        throw new NotSupportedException(
            $"Array element type {elementType.FullName} is not supported for compiled-query parameters.");
    }

    private static IIncludeReader[] BuildIncludeReaders(MemberInfo[] members, IMartenSession session, object query)
    {
        var result = new IIncludeReader[members.Length];
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            var memberType = member switch
            {
                PropertyInfo p => p.PropertyType,
                FieldInfo f => f.FieldType,
                _ => throw new InvalidOperationException(
                    $"Include member {member.DeclaringType?.FullName}.{member.Name} is neither a property nor a field.")
            };

            var value = CompiledQueryMemberReader.Read(member, query)
                ?? throw new InvalidOperationException(
                    $"Include member {member.DeclaringType?.FullName}.{member.Name} returned null at compiled-query " +
                    "execution time; the consumer is expected to initialize the include target before calling session.Query(...).");

            result[i] = BuildIncludeReader(memberType, value, session, member);
        }

        return result;
    }

    private static IIncludeReader BuildIncludeReader(Type memberType, object value, IMartenSession session, MemberInfo member)
    {
        // Mirrors the source-generator emit at
        // CompiledQuerySourceGenerator.AppendIncludeReaderCtor — dispatch by
        // the declared member type onto one of three Include factory methods.
        // The runtime path pays one MakeGenericMethod call per include
        // member per session.Query(...) invocation; cheap relative to the
        // SQL round-trip that follows.
        if (memberType.IsGenericType)
        {
            var def = memberType.GetGenericTypeDefinition();
            var args = memberType.GetGenericArguments();

            // Dispatch first by arity so IList<>.MakeGenericType isn't called with
            // a 2-element type-arg array when the member is Dictionary<TId, TDoc>.
            if (args.Length == 1)
            {
                if (def == typeof(Action<>))
                {
                    var method = typeof(Include).GetMethod(nameof(Include.ReaderToAction))!.MakeGenericMethod(args[0]);
                    return (IIncludeReader)method.Invoke(null, new object[] { session, value })!;
                }

                if (def == typeof(List<>)
                    || def == typeof(IList<>)
                    || typeof(IList<>).MakeGenericType(args).IsAssignableFrom(memberType))
                {
                    var method = typeof(Include).GetMethod(nameof(Include.ReaderToList))!.MakeGenericMethod(args[0]);
                    return (IIncludeReader)method.Invoke(null, new object[] { session, value })!;
                }
            }
            else if (args.Length == 2)
            {
                if (def == typeof(Dictionary<,>)
                    || def == typeof(IDictionary<,>)
                    || typeof(IDictionary<,>).MakeGenericType(args).IsAssignableFrom(memberType))
                {
                    // Include.ReaderToDictionary<T, TId>(session, IDictionary<TId, T>) —
                    // args[0] = TId (the dictionary's key, the doc id type),
                    // args[1] = T (the dictionary's value, the doc type).
                    var method = typeof(Include).GetMethod(nameof(Include.ReaderToDictionary))!
                        .MakeGenericMethod(args[1], args[0]);
                    return (IIncludeReader)method.Invoke(null, new object[] { session, value })!;
                }
            }
        }

        throw new InvalidOperationException(
            $"Include member {member.DeclaringType?.FullName}.{member.Name} has type {memberType.FullName}; " +
            "expected Action<TDoc>, IList<TDoc> / List<TDoc>, or IDictionary<TId, TDoc> / Dictionary<TId, TDoc>.");
    }
}
