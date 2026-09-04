#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Documents;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Storage;
using Weasel.Postgresql;

namespace Marten.Events.Querying;

/*
 * jasperfx#740 (marten#5333): IReadOnlyEventStore.QueryStreamStates() — the streams table as a real
 * IQueryable<StreamState>, executed through the shared IDocumentQueryExecutor hook.
 *
 * This is a DEDICATED provider rather than a StreamState mapping over Marten's full LINQ engine,
 * deliberately. The contract's minimum translatable set includes two shapes the general engine does
 * not translate: member-to-member arithmetic in a comparison (Version - CompactedVersion > N, the
 * compaction-policy predicate — SimpleExpression only reduces constant arithmetic) and equality of
 * a Type-valued member against a typeof constant (AggregateType == typeof(X), which must resolve to
 * the stored aggregate alias). A purpose-built translator over one seven-column table keeps both
 * exact, and keeps the contract's refusal rule enforceable: anything outside the translatable set
 * throws naming the member or operator — never a silent match-all.
 */

/// <summary>
/// The <see cref="IQueryable{T}"/> face of <c>QueryStreamStates()</c>. Composition (Where/OrderBy/
/// ThenBy/Skip/Take) builds expression trees as usual; execution goes through the shared
/// asynchronous terminators in <see cref="DocumentQueryableExtensions"/>, dispatching to
/// <see cref="StreamStateQueryProvider"/>'s <see cref="IDocumentQueryExecutor"/> implementation.
/// </summary>
internal class StreamStateQueryable: IOrderedQueryable<StreamState>
{
    public StreamStateQueryable(StreamStateQueryProvider provider, Expression expression)
    {
        Provider = provider;
        Expression = expression;
    }

    public Type ElementType => typeof(StreamState);
    public Expression Expression { get; }
    public IQueryProvider Provider { get; }

    public IEnumerator<StreamState> GetEnumerator() =>
        throw StreamStateQueryProvider.SynchronousExecutionRefused();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// LINQ provider + shared async execution hook for <see cref="StreamStateQueryable"/>. Translates
/// the supported operator set to SQL over <c>mt_streams</c> and executes through the owning
/// session, hydrating rows with the same <see cref="ISelector{T}"/> the fetch path uses so the two
/// surfaces can never disagree about a column.
/// </summary>
internal class StreamStateQueryProvider: IQueryProvider, IDocumentQueryExecutor
{
    private readonly QuerySession _session;
    private readonly DocumentStore _store;
    private readonly Tenant _tenant;
    private readonly string? _tenantIdOverride;

    public StreamStateQueryProvider(QuerySession session, DocumentStore store, Tenant tenant,
        string? tenantIdOverride)
    {
        _session = session;
        _store = store;
        _tenant = tenant;
        _tenantIdOverride = tenantIdOverride;
    }

    private string effectiveTenantId => _tenantIdOverride ?? _tenant.TenantId;

    public IQueryable<StreamState> CreateRoot() =>
        new StreamStateQueryable(this, Expression.Constant(new StreamStateQueryable(this, null!), typeof(IQueryable<StreamState>)));

    public IQueryable CreateQuery(Expression expression) => CreateQuery<StreamState>(expression);

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        if (typeof(TElement) != typeof(StreamState))
        {
            throw new NotSupportedException(
                $"IReadOnlyEventStore.QueryStreamStates() cannot project to '{typeof(TElement).Name}' — the queryable supports Where, OrderBy/OrderByDescending/ThenBy/ThenByDescending, Skip and Take over StreamState only, not Select projections.");
        }

        return (IQueryable<TElement>)(object)new StreamStateQueryable(this, expression);
    }

    public object? Execute(Expression expression) => throw SynchronousExecutionRefused();

    public TResult Execute<TResult>(Expression expression) => throw SynchronousExecutionRefused();

    internal static NotSupportedException SynchronousExecutionRefused() =>
        new(
            "Synchronous LINQ execution is not supported by IReadOnlyEventStore.QueryStreamStates(). Execute with the asynchronous terminators in JasperFx.Events.Documents.DocumentQueryableExtensions — ToListAsync, CountAsync, AnyAsync or FirstOrDefaultAsync.");

    // ---- IDocumentQueryExecutor ----

    public async Task<IReadOnlyList<T>> ExecuteToListAsync<T>(IQueryable<T> queryable, CancellationToken token)
    {
        var states = await executeListAsync(translate(queryable.Expression), token).ConfigureAwait(false);
        return (IReadOnlyList<T>)states;
    }

    public async Task<T?> ExecuteFirstOrDefaultAsync<T>(IQueryable<T> queryable, CancellationToken token)
    {
        var plan = translate(queryable.Expression);
        plan.Take = plan.Take.HasValue ? Math.Min(plan.Take.Value, 1) : 1;

        var states = await executeListAsync(plan, token).ConfigureAwait(false);
        return (T?)(object?)states.FirstOrDefault();
    }

    public async Task<int> ExecuteCountAsync<T>(IQueryable<T> queryable, CancellationToken token)
    {
        var plan = translate(queryable.Expression);
        await ensureStorageAsync(token).ConfigureAwait(false);

        var handler = new ScalarQueryHandler<long>(builder => writeCountSql(builder, plan));
        var count = await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
        return (int)count;
    }

    public async Task<bool> ExecuteAnyAsync<T>(IQueryable<T> queryable, CancellationToken token)
    {
        var plan = translate(queryable.Expression);
        await ensureStorageAsync(token).ConfigureAwait(false);

        var handler = new ScalarQueryHandler<bool>(builder => writeAnySql(builder, plan));
        return await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    // ---- execution ----

    private async Task ensureStorageAsync(CancellationToken token) =>
        await _tenant.Database.EnsureStorageExistsAsync(typeof(StreamAction), token).ConfigureAwait(false);

    private async Task<IReadOnlyList<StreamState>> executeListAsync(StreamStateQueryPlan plan,
        CancellationToken token)
    {
        await ensureStorageAsync(token).ConfigureAwait(false);

        var handler = new StreamStateListQueryHandler(builder => writeSelectSql(builder, plan));
        return await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    private StreamStateQueryPlan translate(Expression expression) =>
        new StreamStateQueryTranslator(_store.Events).Translate(expression);

    // ---- SQL emission ----

    private void writeParts(ICommandBuilder builder, IReadOnlyList<object> parts)
    {
        foreach (var part in parts)
        {
            if (part is SqlParam p)
            {
                builder.AppendParameter(p.Value);
            }
            else
            {
                builder.Append((string)part);
            }
        }
    }

    private void writeFromAndWhere(ICommandBuilder builder, StreamStateQueryPlan plan)
    {
        builder.Append($"from {_store.Events.DatabaseSchemaName}.{StreamsTable.TableName} where tenant_id = ");
        builder.AppendParameter(effectiveTenantId);

        foreach (var predicate in plan.Predicates)
        {
            builder.Append(" and (");
            writeParts(builder, predicate);
            builder.Append(')');
        }
    }

    private void writeOrderingAndPaging(ICommandBuilder builder, StreamStateQueryPlan plan)
    {
        var orderings = plan.Orderings.Where(x => x.Parts.Count > 0).ToList();
        if (orderings.Count > 0)
        {
            builder.Append(" order by ");
            for (var i = 0; i < orderings.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                writeParts(builder, orderings[i].Parts);
                builder.Append(orderings[i].Descending ? " desc" : " asc");
            }
        }

        if (plan.Skip is { } skip)
        {
            builder.Append(" offset ");
            builder.AppendParameter((long)skip);
        }

        if (plan.Take is { } take)
        {
            builder.Append(" limit ");
            builder.AppendParameter((long)take);
        }
    }

    private void writeSelectSql(ICommandBuilder builder, StreamStateQueryPlan plan)
    {
        // The column list and ordering are owned by StreamStateSql/ISelector<StreamState> — the
        // same row shape the fetch path reads, so the two surfaces cannot drift apart.
        builder.Append(
            $"select id, version, type, timestamp, created, is_archived, {StreamsTable.CompactedVersionColumn} ");
        writeFromAndWhere(builder, plan);
        writeOrderingAndPaging(builder, plan);
    }

    private void writeCountSql(ICommandBuilder builder, StreamStateQueryPlan plan)
    {
        if (plan.Skip is null && plan.Take is null)
        {
            builder.Append("select count(*) ");
            writeFromAndWhere(builder, plan);
            return;
        }

        // A count after Skip/Take counts the page, so wrap the paged selection.
        builder.Append("select count(*) from (select 1 ");
        writeFromAndWhere(builder, plan);
        writeOrderingAndPaging(builder, plan);
        builder.Append(") as paged");
    }

    private void writeAnySql(ICommandBuilder builder, StreamStateQueryPlan plan)
    {
        builder.Append("select exists(select 1 ");
        writeFromAndWhere(builder, plan);

        // Ordering cannot change existence; Skip/Take can.
        var ordered = plan.Orderings;
        plan.Orderings = new List<StreamStateOrdering>();
        writeOrderingAndPaging(builder, plan);
        plan.Orderings = ordered;

        builder.Append(')');
    }
}

/// <summary>One SQL parameter value inside a translated fragment.</summary>
internal readonly record struct SqlParam(object Value);

internal class StreamStateOrdering
{
    public StreamStateOrdering(IReadOnlyList<object> parts, bool descending)
    {
        Parts = parts;
        Descending = descending;
    }

    /// <summary>Empty when the ordering term is a stable constant (the inapplicable identity member) and can be skipped.</summary>
    public IReadOnlyList<object> Parts { get; }

    public bool Descending { get; }
}

internal class StreamStateQueryPlan
{
    public List<IReadOnlyList<object>> Predicates { get; } = new();
    public List<StreamStateOrdering> Orderings { get; set; } = new();
    public int? Skip { get; set; }
    public int? Take { get; set; }
}

/// <summary>
/// Translates the supported LINQ operator set over <see cref="StreamState"/> into SQL fragments
/// against <c>mt_streams</c>. Anything outside the contract's translatable set throws
/// <see cref="NotSupportedException"/> naming the member or operator — the jasperfx#737/#740 rule:
/// a predicate that cannot be honored must be refused, never silently dropped.
/// </summary>
internal class StreamStateQueryTranslator
{
    private readonly EventGraph _events;

    public StreamStateQueryTranslator(EventGraph events)
    {
        _events = events;
    }

    public StreamStateQueryPlan Translate(Expression expression)
    {
        var plan = new StreamStateQueryPlan();

        // Unwind the operator chain source-first.
        var calls = new List<MethodCallExpression>();
        var current = expression;
        while (current is MethodCallExpression mc)
        {
            calls.Insert(0, mc);
            current = mc.Arguments[0];
        }

        foreach (var call in calls)
        {
            switch (call.Method.Name)
            {
                case nameof(Queryable.Where):
                    plan.Predicates.Add(translatePredicate(unquoteLambda(call.Arguments[1])));
                    break;

                case nameof(Queryable.OrderBy):
                    plan.Orderings = new List<StreamStateOrdering> { ordering(call, descending: false) };
                    break;

                case nameof(Queryable.OrderByDescending):
                    plan.Orderings = new List<StreamStateOrdering> { ordering(call, descending: true) };
                    break;

                case nameof(Queryable.ThenBy):
                    plan.Orderings.Add(ordering(call, descending: false));
                    break;

                case nameof(Queryable.ThenByDescending):
                    plan.Orderings.Add(ordering(call, descending: true));
                    break;

                case nameof(Queryable.Skip):
                    plan.Skip = (plan.Skip ?? 0) + evaluateInt(call.Arguments[1]);
                    break;

                case nameof(Queryable.Take):
                    var take = evaluateInt(call.Arguments[1]);
                    plan.Take = plan.Take.HasValue ? Math.Min(plan.Take.Value, take) : take;
                    break;

                default:
                    throw new NotSupportedException(
                        $"The LINQ operator '{call.Method.Name}' is not supported by IReadOnlyEventStore.QueryStreamStates(). " +
                        "Supported: Where, OrderBy/OrderByDescending/ThenBy/ThenByDescending, Skip, Take, and the asynchronous terminators in JasperFx.Events.Documents.DocumentQueryableExtensions.");
            }
        }

        return plan;
    }

    private StreamStateOrdering ordering(MethodCallExpression call, bool descending)
    {
        var lambda = unquoteLambda(call.Arguments[1]);
        var body = stripConvert(lambda.Body);

        if (body is MemberExpression { Expression: ParameterExpression } member)
        {
            // The identity member that does not apply to the store's identity style holds its
            // default on every row — a stable constant, skipped rather than emitted.
            if (isInapplicableIdentity(member.Member.Name))
            {
                return new StreamStateOrdering(Array.Empty<object>(), descending);
            }

            return new StreamStateOrdering(new object[] { columnFor(member.Member.Name) }, descending);
        }

        throw new NotSupportedException(
            $"QueryStreamStates() can only order by a direct StreamState member, not '{body}'.");
    }

    private static LambdaExpression unquoteLambda(Expression e)
    {
        if (e is UnaryExpression { NodeType: ExpressionType.Quote } quote)
        {
            e = quote.Operand;
        }

        var lambda = (LambdaExpression)e;
        if (lambda.Parameters.Count != 1)
        {
            throw new NotSupportedException(
                "The indexed Where/OrderBy overloads are not supported by QueryStreamStates().");
        }

        return lambda;
    }

    private static int evaluateInt(Expression e) => (int)evaluate(e)!;

    private List<object> translatePredicate(LambdaExpression lambda) => translateBoolean(lambda.Body);

    private List<object> translateBoolean(Expression e)
    {
        e = stripConvert(e);

        switch (e)
        {
            case BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.And } and:
            {
                var parts = new List<object> { "(" };
                parts.AddRange(translateBoolean(and.Left));
                parts.Add(" and ");
                parts.AddRange(translateBoolean(and.Right));
                parts.Add(")");
                return parts;
            }

            case BinaryExpression { NodeType: ExpressionType.OrElse or ExpressionType.Or } or:
            {
                var parts = new List<object> { "(" };
                parts.AddRange(translateBoolean(or.Left));
                parts.Add(" or ");
                parts.AddRange(translateBoolean(or.Right));
                parts.Add(")");
                return parts;
            }

            case UnaryExpression { NodeType: ExpressionType.Not } not:
            {
                var parts = new List<object> { "NOT (" };
                parts.AddRange(translateBoolean(not.Operand));
                parts.Add(")");
                return parts;
            }

            case BinaryExpression binary when comparisonOperator(binary.NodeType) != null:
                return translateComparison(binary);

            case MemberExpression { Expression: ParameterExpression } member when member.Type == typeof(bool):
                return new List<object> { columnSqlOrParam(member.Member.Name), " = TRUE" };

            case ConstantExpression { Value: bool b }:
                return new List<object> { b ? "TRUE" : "FALSE" };

            default:
                if (!ReferencesParameter.Test(e) && e.Type == typeof(bool))
                {
                    return new List<object> { (bool)evaluate(e)! ? "TRUE" : "FALSE" };
                }

                throw new NotSupportedException(
                    $"QueryStreamStates() cannot translate the predicate expression '{e}'.");
        }
    }

    private static string? comparisonOperator(ExpressionType type) => type switch
    {
        ExpressionType.Equal => "=",
        ExpressionType.NotEqual => "<>",
        ExpressionType.GreaterThan => ">",
        ExpressionType.GreaterThanOrEqual => ">=",
        ExpressionType.LessThan => "<",
        ExpressionType.LessThanOrEqual => "<=",
        _ => null
    };

    private List<object> translateComparison(BinaryExpression binary)
    {
        var op = comparisonOperator(binary.NodeType)!;
        var left = stripConvert(binary.Left);
        var right = stripConvert(binary.Right);

        // AggregateType == typeof(X) / != typeof(X) / == null — the compaction-policy selector.
        // Resolved against the stored aggregate alias, exactly what StartStream<T> writes.
        if (isAggregateTypeMember(left) || isAggregateTypeMember(right))
        {
            var other = isAggregateTypeMember(left) ? right : left;

            if (op is not ("=" or "<>"))
            {
                throw new NotSupportedException(
                    "StreamState.AggregateType only supports equality comparison against a Type value in QueryStreamStates().");
            }

            if (ReferencesParameter.Test(other))
            {
                throw new NotSupportedException(
                    $"QueryStreamStates() cannot translate the AggregateType comparison '{binary}'.");
            }

            var typeValue = (Type?)evaluate(other);
            if (typeValue == null)
            {
                return new List<object> { op == "=" ? "type is null" : "type is not null" };
            }

            var alias = _events.AggregateAliasFor(typeValue);
            return op == "="
                ? new List<object> { "type = ", new SqlParam(alias) }
                // IS DISTINCT FROM so a stream started with no aggregate type still counts as
                // "not X", matching the C# semantics of a null Type against a typeof constant.
                : new List<object> { "type is distinct from ", new SqlParam(alias) };
        }

        // Null comparisons become IS [NOT] NULL on the other operand.
        if (isNullConstant(left) || isNullConstant(right))
        {
            var operand = isNullConstant(left) ? right : left;
            var parts = translateOperand(operand);
            parts.Add(op == "=" ? " is null" : " is not null");
            return parts;
        }

        var result = translateOperand(left);
        if (op == "<>")
        {
            // IS DISTINCT FROM matches C# inequality when the column is null (Key on rows that
            // never had one, an untyped stream's type column).
            result.Add(" is distinct from ");
        }
        else
        {
            result.Add($" {op} ");
        }

        result.AddRange(translateOperand(right));
        return result;
    }

    private List<object> translateOperand(Expression e)
    {
        e = stripConvert(e);

        if (!ReferencesParameter.Test(e))
        {
            return new List<object> { new SqlParam(evaluate(e) ?? DBNull.Value) };
        }

        switch (e)
        {
            case MemberExpression { Expression: ParameterExpression } member:
                return new List<object> { columnSqlOrParam(member.Member.Name) };

            case BinaryExpression binary when binary.NodeType is ExpressionType.Add or ExpressionType.Subtract
                or ExpressionType.Multiply or ExpressionType.Divide:
            {
                var op = binary.NodeType switch
                {
                    ExpressionType.Add => " + ",
                    ExpressionType.Subtract => " - ",
                    ExpressionType.Multiply => " * ",
                    _ => " / "
                };

                var parts = new List<object> { "(" };
                parts.AddRange(translateOperand(binary.Left));
                parts.Add(op);
                parts.AddRange(translateOperand(binary.Right));
                parts.Add(")");
                return parts;
            }

            default:
                throw new NotSupportedException(
                    $"QueryStreamStates() cannot translate the expression '{e}'.");
        }
    }

    private static bool isAggregateTypeMember(Expression e) =>
        stripConvert(e) is MemberExpression { Expression: ParameterExpression } m &&
        m.Member.Name == nameof(StreamState.AggregateType);

    private static bool isNullConstant(Expression e) =>
        stripConvert(e) is ConstantExpression { Value: null };

    private static Expression stripConvert(Expression e)
    {
        while (e is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u)
        {
            e = u.Operand;
        }

        return e;
    }

    private static object? evaluate(Expression e)
    {
        if (e is ConstantExpression c)
        {
            return c.Value;
        }

        return Expression.Lambda(e).Compile().DynamicInvoke();
    }

    private bool isInapplicableIdentity(string memberName) =>
        (memberName == nameof(StreamState.Id) && _events.StreamIdentity == StreamIdentity.AsString) ||
        (memberName == nameof(StreamState.Key) && _events.StreamIdentity == StreamIdentity.AsGuid);

    /// <summary>
    /// The member-to-column map — the whole translatable member surface. An inapplicable identity
    /// member holds its CLR default on every row, so it translates to that constant; everything
    /// unknown is refused by name.
    /// </summary>
    private object columnSqlOrParam(string memberName)
    {
        if (memberName == nameof(StreamState.Id) && _events.StreamIdentity == StreamIdentity.AsString)
        {
            return new SqlParam(Guid.Empty);
        }

        if (memberName == nameof(StreamState.Key) && _events.StreamIdentity == StreamIdentity.AsGuid)
        {
            return "null::varchar";
        }

        return columnFor(memberName);
    }

    private string columnFor(string memberName) => memberName switch
    {
        nameof(StreamState.Id) => "id",
        nameof(StreamState.Key) => "id",
        nameof(StreamState.Version) => "version",
        nameof(StreamState.LastTimestamp) => "timestamp",
        nameof(StreamState.Created) => "created",
        nameof(StreamState.IsArchived) => "is_archived",
        nameof(StreamState.CompactedVersion) => StreamsTable.CompactedVersionColumn,
        nameof(StreamState.AggregateType) => throw new NotSupportedException(
            "StreamState.AggregateType only supports equality comparison against a Type value in QueryStreamStates()."),
        _ => throw new NotSupportedException(
            $"QueryStreamStates() cannot translate the member 'StreamState.{memberName}'.")
    };

    /// <summary>Does the expression reference the lambda parameter anywhere?</summary>
    private class ReferencesParameter: ExpressionVisitor
    {
        private bool _found;

        public static bool Test(Expression e)
        {
            var visitor = new ReferencesParameter();
            visitor.Visit(e);
            return visitor._found;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            _found = true;
            return base.VisitParameter(node);
        }
    }
}

/// <summary>
/// Reads a page of <c>mt_streams</c> rows through the SAME <see cref="ISelector{T}"/> the fetch
/// path uses (<c>IEventStorage</c>'s <c>ISelector&lt;StreamState&gt;</c>), so the queryable and
/// <c>FetchStreamStateAsync</c> can never disagree about column ordering or hydration.
/// </summary>
internal class StreamStateListQueryHandler: IQueryHandler<IReadOnlyList<StreamState>>
{
    private readonly Action<ICommandBuilder> _configure;

    public StreamStateListQueryHandler(Action<ICommandBuilder> configure)
    {
        _configure = configure;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session) => _configure(builder);

    public async Task<IReadOnlyList<StreamState>> HandleAsync(DbDataReader reader, IStorageSession session,
        CancellationToken token)
    {
        var selector = (ISelector<StreamState>)((IMartenSession)session).EventStorage();

        var states = new List<StreamState>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            states.Add(await selector.ResolveAsync(reader, token).ConfigureAwait(false));
        }

        return states;
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token) =>
        throw new NotSupportedException();
}

internal class ScalarQueryHandler<T>: IQueryHandler<T>
{
    private readonly Action<ICommandBuilder> _configure;

    public ScalarQueryHandler(Action<ICommandBuilder> configure)
    {
        _configure = configure;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session) => _configure(builder);

    public async Task<T> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return default!;
        }

        return await reader.GetFieldValueAsync<T>(0, token).ConfigureAwait(false);
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token) =>
        throw new NotSupportedException();
}
