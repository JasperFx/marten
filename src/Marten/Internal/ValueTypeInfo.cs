using System;
using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;
using FastExpressionCompiler;
using JasperFx.Core.Reflection;
using Marten.Events;
using Marten.Linq.Members.ValueCollections;

namespace Marten.Internal;

/// <summary>
/// Internal model of a custom "wrapped" value type Marten uses
/// for LINQ generation
/// </summary>
public class ValueTypeInfo
{
    private object _converter;
    public Type OuterType { get; }
    public Type SimpleType { get; }
    public PropertyInfo ValueProperty { get; }
    public MethodInfo Builder { get; }
    public ConstructorInfo Ctor { get; }

    public ValueTypeInfo(Type outerType, Type simpleType, PropertyInfo valueProperty, ConstructorInfo ctor)
    {
        OuterType = outerType;
        SimpleType = simpleType;
        ValueProperty = valueProperty;
        Ctor = ctor;
    }

    public ValueTypeInfo(Type outerType, Type simpleType, PropertyInfo valueProperty, MethodInfo builder)
    {
        OuterType = outerType;
        SimpleType = simpleType;
        ValueProperty = valueProperty;
        Builder = builder;
    }

    public Func<TInner, TOuter> CreateConverter<TOuter, TInner>()
    {
        if (_converter != null)
        {
            return (Func<TInner, TOuter>)_converter;
        }

        var inner = Expression.Parameter(typeof(TInner), "inner");
        Expression builder;
        if (Builder != null)
        {
            builder = Expression.Call(null, Builder, inner);
        }
        else if (Ctor != null)
        {
            builder = Expression.New(Ctor, inner);
        }
        else
        {
            throw new NotSupportedException("Marten cannot build a type converter for strong typed id type " +
                                            OuterType.FullNameInCode());
        }

        var lambda = Expression.Lambda<Func<TInner, TOuter>>(builder, inner);

        _converter = lambda.CompileFast();
        return (Func<TInner, TOuter>)_converter;
    }

    public Func<TOuter, TInner> ValueAccessor<TOuter, TInner>()
    {
        var outer = Expression.Parameter(typeof(TOuter), "outer");
        var getter = ValueProperty.GetMethod;
        var lambda = Expression.Lambda<Func<TOuter, TInner>>(Expression.Call(outer, getter), outer);
        return lambda.CompileFast();
    }

    public Func<IEvent,TId> CreateAggregateIdentitySource<TId>() where TId : notnull
    {
        var e = Expression.Parameter(typeof(IEvent), "e");
        var eMember = SimpleType == typeof(Guid)
            ? ReflectionHelper.GetProperty<IEvent>(x => x.StreamId)
            : ReflectionHelper.GetProperty<IEvent>(x => x.StreamKey);

        var raw = Expression.Call(e, eMember.GetMethod);
        Expression wrapped = null;
        if (Builder != null)
        {
            wrapped = Expression.Call(null, Builder, raw);
        }
        else if (Ctor != null)
        {
            wrapped = Expression.New(Ctor, raw);
        }
        else
        {
            throw new NotSupportedException("Marten cannot build a type converter for strong typed id type " +
                                            OuterType.FullNameInCode());
        }

        var lambda = Expression.Lambda<Func<IEvent, TId>>(wrapped, e);

        return lambda.CompileFast();
    }
}

internal class ValueTypeElementMember: ElementMember
{
    public ValueTypeElementMember(Type declaringType, Type reflectedType) : base(declaringType, reflectedType)
    {
    }
}
