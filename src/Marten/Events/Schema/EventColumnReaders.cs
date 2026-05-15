#nullable enable
using System;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Linq.Parsing;

namespace Marten.Events.Schema;

/// <summary>
/// Helpers that build compiled-delegate reader functions from the same
/// member expressions <c>EventTableColumn</c> already uses to declare its
/// column. Used by the closed-shape event-storage hierarchy (#4410 W4)
/// so the read path never calls reflection per row.
/// </summary>
/// <remarks>
/// <para>
/// The codegen path emits direct C# (<c>@event.Member = reader.GetFieldValue&lt;T&gt;(index);</c>)
/// inline into the generated <c>ApplyReaderDataToEvent</c> body. The
/// closed-shape equivalent compiles the same statement to a delegate
/// <i>once</i> at column construction time. Per-row cost is one virtual
/// call (<see cref="IEventTableColumn.ReadValueSync"/>) + one direct
/// member write — no reflection, no boxing for value-typed members.
/// </para>
/// </remarks>
internal static class EventColumnReaders
{
    /// <summary>
    /// Builds a sync reader from a member-access expression like
    /// <c>x =&gt; x.Id</c>. The returned delegate is equivalent to:
    /// <code>
    /// (reader, index, @event) =&gt; @event.Id = reader.GetFieldValue&lt;Guid&gt;(index);
    /// </code>
    /// Caller is responsible for the <c>IsDBNull</c> check before invocation.
    /// </summary>
    public static Action<DbDataReader, int, IEvent> BuildSync(Expression<Func<IEvent, object>> memberExpression)
    {
        var member = MemberFinder.Determine(memberExpression).Single();
        var memberType = member.GetMemberType()!;

        var readerParam = Expression.Parameter(typeof(DbDataReader), "reader");
        var indexParam = Expression.Parameter(typeof(int), "index");
        var eventParam = Expression.Parameter(typeof(IEvent), "@event");

        var getFieldValueMethod = typeof(DbDataReader)
            .GetMethod(nameof(DbDataReader.GetFieldValue))!
            .MakeGenericMethod(memberType);

        var getValueCall = Expression.Call(readerParam, getFieldValueMethod, indexParam);

        var memberAccess = Expression.MakeMemberAccess(eventParam, member);
        var assign = Expression.Assign(memberAccess, getValueCall);

        return Expression.Lambda<Action<DbDataReader, int, IEvent>>(
            assign, readerParam, indexParam, eventParam).Compile();
    }

    /// <summary>
    /// Builds an async reader from the same member expression. The
    /// returned delegate awaits <see cref="DbDataReader.GetFieldValueAsync{T}(int, CancellationToken)"/>
    /// then assigns the result.
    /// </summary>
    /// <remarks>
    /// Returns a delegate over an actual <c>async</c> method (compiled
    /// lambda + state machine) rather than a manually-built expression
    /// tree because async/await synthesis is too involved to reproduce
    /// with <see cref="Expression"/>. The compiled body is the standard
    /// "await GetFieldValueAsync; assign" pattern.
    /// </remarks>
    public static Func<DbDataReader, int, IEvent, CancellationToken, Task> BuildAsync(
        Expression<Func<IEvent, object>> memberExpression)
    {
        var member = MemberFinder.Determine(memberExpression).Single();
        var memberType = member.GetMemberType()!;

        // We call BuildAsyncImpl<T> with the right T via reflection (once,
        // at startup). BuildAsyncImpl returns a delegate that's strongly
        // typed and doesn't box.
        var helper = typeof(EventColumnReaders)
            .GetMethod(nameof(BuildAsyncImpl), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(memberType);

        return (Func<DbDataReader, int, IEvent, CancellationToken, Task>)helper.Invoke(null, new object[] { member })!;
    }

    private static Func<DbDataReader, int, IEvent, CancellationToken, Task> BuildAsyncImpl<T>(MemberInfo member)
    {
        // Compile a setter once: (e, v) => e.Member = (T)v
        var eventParam = Expression.Parameter(typeof(IEvent), "@event");
        var valueParam = Expression.Parameter(typeof(T), "value");
        var memberAccess = Expression.MakeMemberAccess(eventParam, member);
        var assign = Expression.Assign(memberAccess, valueParam);
        var setter = Expression.Lambda<Action<IEvent, T>>(assign, eventParam, valueParam).Compile();

        // The returned delegate awaits the typed GetFieldValueAsync<T> and assigns.
        return async (reader, index, @event, cancellation) =>
        {
            var value = await reader.GetFieldValueAsync<T>(index, cancellation).ConfigureAwait(false);
            setter(@event, value);
        };
    }
}
