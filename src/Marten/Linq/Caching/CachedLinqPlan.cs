#nullable enable
using System;
using System.Collections.Generic;
using Marten.Linq.QueryHandlers;
using Npgsql;

namespace Marten.Linq.Caching;

/// <summary>
///     A single compiled-and-cached LINQ plan for one structural query shape: the query
///     handler used to read results (safe to reuse across calls -- selectors only depend
///     on the shape, never on parameter values), a template <see cref="NpgsqlBatch" /> with
///     unique sentinel parameter values recorded once, and the mapping from each parameter
///     back to the slot (position in <see cref="ExpressionShapeVisitor.Slots" />) that
///     supplied it.
/// </summary>
internal sealed class CachedLinqPlan
{
    public required IQueryHandler Handler { get; init; }
    public required NpgsqlBatch TemplateBatch { get; init; }
    public required IReadOnlyList<SlotBinding> Bindings { get; init; }
    public required IReadOnlyList<Type> DocumentTypes { get; init; }
}

/// <summary>
///     Records that the parameter at <see cref="ParameterIndex" /> within the batch command
///     at <see cref="CommandIndex" /> should be rebound, on every cache hit, from the
///     current value of the expression slot at <see cref="SlotIndex" />.
/// </summary>
internal readonly record struct SlotBinding(int CommandIndex, int ParameterIndex, int SlotIndex);

