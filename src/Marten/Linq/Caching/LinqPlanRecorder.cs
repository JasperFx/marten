#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Internal;
using Marten.Internal.CompiledQueries;
using Marten.Internal.Sessions;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Util;
using Npgsql;

namespace Marten.Linq.Caching;

/// <summary>
///     Builds a <see cref="CachedLinqPlan" /> for a query shape on a cache miss.
/// </summary>
/// <remarks>
///     The approach mirrors how Marten already compiles <c>ICompiledQuery</c> plans
///     (<see cref="CompiledQueryPlan" />): a "template" expression is built by replacing
///     every value slot with a distinct, never-otherwise-occurring sentinel value (reusing
///     the same <see cref="UniqueValueSource" /> / <see cref="QueryCompiler.Finders" />
///     machinery). That template is compiled through the normal LINQ pipeline exactly once,
///     and the resulting command's parameters are matched back to slots purely by sentinel
///     value identity -- no reflection into Weasel's (immutable) <c>CommandParameter</c> or
///     Marten's internal filter types is required.
///     <para>
///         If any command parameter can't be confidently attributed to a slot (for example
///         a conjoined multi-tenancy tenant id, which is a per-session value rather than a
///         value captured by the query expression), the whole shape is rejected -- it is
///         never cached. This guarantees a cache hit can never replay a stale or foreign
///         parameter value.
///     </para>
/// </remarks>
internal static class LinqPlanRecorder
{
    public static CachedLinqPlan? TryBuild<TResult>(
        MartenLinqQueryProvider provider,
        QuerySession session,
        Expression realExpression,
        ExpressionShapeVisitor shape,
        Func<LinqQueryParser, IQueryHandler<TResult>> buildHandler)
    {
        if (!shape.IsSupported || shape.Slots.Count == 0)
        {
            return null;
        }

        var sentinelValues = new object?[shape.Slots.Count];
        var valueSource = new UniqueValueSource();

        for (var i = 0; i < shape.Slots.Count; i++)
        {
            var slotType = shape.Slots[i].Type;
            var clrType = Nullable.GetUnderlyingType(slotType) ?? slotType;

            if (!QueryCompiler.Finders.Any(f => f.Matches(clrType)))
            {
                // No known way to manufacture a unique sentinel value for this CLR type
                // (e.g. bool) -- don't cache.
                return null;
            }

            try
            {
                sentinelValues[i] = valueSource.GetValue(clrType);
            }
            catch (Exception)
            {
                return null;
            }
        }

        Expression templateExpression;
        try
        {
            templateExpression = new SlotReplacingVisitor(shape.Slots, sentinelValues).Visit(realExpression)!;
        }
        catch (Exception)
        {
            return null;
        }

        LinqQueryParser parser;
        IQueryHandler<TResult> handler;
        Type[] documentTypes;
        NpgsqlBatch templateBatch;

        try
        {
            parser = new LinqQueryParser(provider, session, templateExpression);
            handler = buildHandler(parser);
            documentTypes = parser.DocumentTypes().ToArray();
            templateBatch = ((IMartenSession)session).BuildCommand(handler);
        }
        catch (Exception)
        {
            return null;
        }

        var bindings = new List<SlotBinding>();
        var usedSlots = new bool[sentinelValues.Length];

        for (var ci = 0; ci < templateBatch.BatchCommands.Count; ci++)
        {
            var command = templateBatch.BatchCommands[ci];
            for (var pi = 0; pi < command.Parameters.Count; pi++)
            {
                var value = command.Parameters[pi].Value;
                var matchedSlot = -1;

                for (var si = 0; si < sentinelValues.Length; si++)
                {
                    if (usedSlots[si])
                    {
                        continue;
                    }

                    if (Equals(value, sentinelValues[si]))
                    {
                        matchedSlot = si;
                        break;
                    }
                }

                if (matchedSlot < 0)
                {
                    // A parameter we can't attribute to one of our captured slots (tenant
                    // id, other session-scoped values, etc.) -- refuse to cache rather than
                    // risk replaying a stale or foreign value on a future hit.
                    return null;
                }

                usedSlots[matchedSlot] = true;
                bindings.Add(new SlotBinding(ci, pi, matchedSlot));
            }
        }

        return new CachedLinqPlan
        {
            Handler = handler,
            TemplateBatch = templateBatch,
            Bindings = bindings,
            DocumentTypes = documentTypes
        };
    }

    /// <summary>
    ///     Builds a fresh <see cref="NpgsqlBatch" /> from a cached plan's template, replacing
    ///     each parameter's value with the current slot value supplied by the caller. This
    ///     is the whole point of the cache: no LINQ parsing, no SQL generation -- just a
    ///     cheap clone-and-rebind.
    /// </summary>
    public static NpgsqlBatch RebindValues(CachedLinqPlan plan, IReadOnlyList<object?> currentValues)
    {
        var batch = new NpgsqlBatch();

        for (var ci = 0; ci < plan.TemplateBatch.BatchCommands.Count; ci++)
        {
            var source = plan.TemplateBatch.BatchCommands[ci];
            var command = new NpgsqlBatchCommand(source.CommandText);

            for (var pi = 0; pi < source.Parameters.Count; pi++)
            {
                var binding = findBinding(plan.Bindings, ci, pi);
                var sourceParameter = source.Parameters[pi];

                command.Parameters.Add(new NpgsqlParameter
                {
                    ParameterName = sourceParameter.ParameterName,
                    Value = currentValues[binding.SlotIndex] ?? DBNull.Value
                });
            }

            batch.BatchCommands.Add(command);
        }

        return batch;
    }

    private static SlotBinding findBinding(IReadOnlyList<SlotBinding> bindings, int commandIndex, int parameterIndex)
    {
        foreach (var binding in bindings)
        {
            if (binding.CommandIndex == commandIndex && binding.ParameterIndex == parameterIndex)
            {
                return binding;
            }
        }

        throw new InvalidOperationException(
            "No slot binding found for a cached plan's parameter -- this is a bug in the query plan cache.");
    }
}

