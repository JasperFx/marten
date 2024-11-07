using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FastExpressionCompiler;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Linq.Parsing;
using Marten.Schema;

namespace Marten.Events.Aggregation;

public enum AggregationScope
{
    /// <summary>
    ///     This aggregation only applies to events from a single stream at a time
    /// </summary>
    SingleStream,

    /// <summary>
    ///     This aggregation applies events from multiple streams to a single aggregate
    /// </summary>
    MultiStream
}

public interface IAggregateVersioning
{
    public MemberInfo VersionMember
    {
        get;
    }

    void TrySetVersion(object aggregate, IEvent lastEvent);
    long GetVersion(object aggregate);
}

public interface IAggregateVersioning<T>
{
    void TrySetVersion(T aggregate, IEvent lastEvent);
}

internal class AggregateVersioning<T>: IAggregateVersioning, IAggregateVersioning<T>, ILiveAggregator<T>
{
    private readonly AggregationScope _scope;
    private readonly Lazy<Action<T, IEvent>> _setValue;


    public AggregateVersioning(AggregationScope scope)
    {
        _setValue = new Lazy<Action<T, IEvent>>(buildAction);
        _scope = scope;

        var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var props = typeof(T).GetProperties(bindingFlags)
            .Where(x => x.CanWrite)
            .Where(x => x.PropertyType == typeof(int) || x.PropertyType == typeof(long))
            .OfType<MemberInfo>();

        var fields = typeof(T).GetFields(bindingFlags)
            .Where(x => x.FieldType == typeof(int) || x.FieldType == typeof(long))
            .OfType<MemberInfo>();

        var members = props.Concat(fields);
        // ReSharper disable once PossibleMultipleEnumeration
        VersionMember = members.FirstOrDefault(x => x.HasAttribute<VersionAttribute>());
        // ReSharper disable once PossibleMultipleEnumeration
        VersionMember ??= members.FirstOrDefault(x =>
            x.Name.EqualsIgnoreCase("version") && !x.HasAttribute<MartenIgnoreAttribute>());
    }

    public ILiveAggregator<T> Inner { get; set; }

    public MemberInfo VersionMember
    {
        get;
        private set;
    }

    void IAggregateVersioning.TrySetVersion(object aggregate, IEvent lastEvent)
    {
        if (aggregate == null || lastEvent == null)
        {
            return;
        }

        TrySetVersion((T)aggregate, lastEvent);
    }

    long IAggregateVersioning.GetVersion(object aggregate)
    {
        return GetVersion((T)aggregate);
    }

    public void TrySetVersion(T aggregate, IEvent lastEvent)
    {
        if (aggregate == null || lastEvent == null)
        {
            return;
        }

        _setValue.Value(aggregate, lastEvent);
    }

    public async ValueTask<T> BuildAsync(IReadOnlyList<IEvent> events, IQuerySession session, T? snapshot,
        CancellationToken cancellation)
    {
        var aggregate = await Inner.BuildAsync(events, session, snapshot, cancellation).ConfigureAwait(false);
        TrySetVersion(aggregate, events.LastOrDefault());
        return aggregate;
    }

    private Action<T, IEvent> buildAction()
    {
        if (VersionMember == null)
        {
            return (_, _) => { };
        }

        var aggregate = Expression.Parameter(typeof(T), "aggregate");
        var @event = Expression.Parameter(typeof(IEvent), "e");

        var eventMethod = _scope == AggregationScope.SingleStream
            ? ReflectionHelper.GetProperty<IEvent>(x => x.Version).GetMethod
            : ReflectionHelper.GetProperty<IEvent>(x => x.Sequence).GetMethod;

        var accessVersion = Expression.Call(@event, eventMethod!);

        if (VersionMember.GetMemberType() == typeof(int))
        {
            accessVersion = Expression.Call(typeof(Convert).GetMethod(nameof(Convert.ToInt32), new[] { typeof(long) }),
                accessVersion);
        }

        var body = determineBody(aggregate, accessVersion);

        var lambda = Expression.Lambda<Action<T, IEvent>>(body, aggregate, @event);
        return lambda.CompileFast();
    }

    private Expression determineBody(ParameterExpression aggregate, MethodCallExpression accessVersion)
    {
        switch (VersionMember)
        {
            case PropertyInfo prop:
                return Expression.Call(aggregate, prop.SetMethod!, accessVersion);
            case FieldInfo field:
            {
                var fieldExpr = Expression.Field(aggregate, field);
                return Expression.Assign(fieldExpr, accessVersion);
            }
            default:
                throw new InvalidOperationException("The Version member must be either a Field or Property");
        }
    }

    public void Override(Expression<Func<T, int>> expression)
    {
        VersionMember = MemberFinder.Determine(expression).Single();
    }

    public void Override(Expression<Func<T, long>> expression)
    {
        VersionMember = MemberFinder.Determine(expression).Single();
    }

    public long GetVersion(T aggregate)
    {
        if (VersionMember is PropertyInfo prop)
        {
            return Convert.ToInt64(prop.GetValue(aggregate));
        }

        return Convert.ToInt64(VersionMember.As<FieldInfo>().GetValue(aggregate));
    }
}
