#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Marten.Util;

internal static class GenericsExtensions
{
    public static bool IsGenericInterfaceImplementation(this object obj, Type openInterfaceType)
    {
        return obj.GetType().GetInterfaces().Any(x => x.IsGenericType && openInterfaceType.IsAssignableFrom(x.GetGenericTypeDefinition()));
    }

    // 9.0 (#4373): cache an unwrap-and-rebox delegate per input enumerable type.
    // The original implementation re-walked the IEnumerable<Nullable<T>> hierarchy,
    // resolved the .Value property, and the strong-typed identity constructor on every
    // IsOneOf LINQ filter. Steady state should hit only the cached unwrapper delegate
    // per element type — first-call cost is the same as today (a single reflection
    // pass plus a tiny lambda allocation that captures the resolved members).
    private static readonly ConcurrentDictionary<Type, Func<object, object>> _unwrapEnumerableOfNullablesCache = new();

    public static object UnwrapIEnumerableOfNullables(this object obj)
    {
        var inputType = obj.GetType();
        var unwrapper = _unwrapEnumerableOfNullablesCache.GetOrAdd(inputType, BuildUnwrapper);
        return unwrapper(obj);
    }

    private static Func<object, object> BuildUnwrapper(Type inputType)
    {
        // Find an IEnumerable<Nullable<T>> interface, if any, on the input type.
        var nullableEnumerableInterface = inputType.GetInterfaces().FirstOrDefault(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition() == typeof(IEnumerable<>) &&
            i.GetGenericArguments()[0].IsGenericType &&
            i.GetGenericArguments()[0].GetGenericTypeDefinition() == typeof(Nullable<>));

        // Not a nullable-element enumerable — return identity (matches the legacy
        // behaviour of falling through to the original `obj`).
        if (nullableEnumerableInterface == null)
        {
            return static x => x;
        }

        // The Nullable<T>'s T IS the strong-typed wrapper (e.g. `record struct Issue2Id(Guid Value)`),
        // NOT the primitive. When a Nullable<Issue2Id> is boxed and iterated, the runtime
        // hands back boxed `Issue2Id` (Nullable<T> boxes to T), so the per-element Value
        // unwrap is done against the wrapper's own `Value` property — which has the same
        // name as Nullable<T>.Value purely by convention.
        var strongIdType = nullableEnumerableInterface
            .GetGenericArguments()[0]   // Nullable<TWrapper>
            .GetGenericArguments()[0];  // TWrapper

        // Pick the wrapper's value-ctor — the single-arg ctor whose parameter type is
        // NOT the wrapper itself. For record-struct wrappers .NET emits both the value
        // ctor `TWrapper(Guid)` and a copy ctor `TWrapper(TWrapper)`; the legacy code
        // got the right one by accident because it filtered by `parameters[0].ParameterType
        // == x.GetType()` per element. We do the equivalent eagerly here so the
        // delegate captures only the value ctor.
        ConstructorInfo? ctor = null;
        Type? primitiveType = null;
        foreach (var c in strongIdType.GetConstructors())
        {
            var parameters = c.GetParameters();
            if (parameters.Length != 1) continue;
            if (parameters[0].ParameterType == strongIdType) continue; // skip copy ctor
            ctor = c;
            primitiveType = parameters[0].ParameterType;
            break;
        }

        MethodInfo? fromMethod = null;
        if (ctor == null)
        {
            // User-defined static builder method (any name) returning the wrapper from
            // a single non-wrapper argument.
            fromMethod = strongIdType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                    m.ReturnType == strongIdType &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType != strongIdType);
            if (fromMethod == null)
            {
                throw new InvalidOperationException(
                    $"Type {strongIdType} does not have a constructor or a static builder method.");
            }
        }

        // The wrapper's own `Value` property unwraps to the primitive. Iterating the
        // input enumerable yields boxed wrapper instances (the runtime peels Nullable<>
        // when boxing), so GetValue(item) here is `Issue2Id.Value` on an `Issue2Id`.
        var valueProp = strongIdType.GetProperty("Value")
            ?? throw new InvalidOperationException(
                $"Type {strongIdType} does not expose a 'Value' property to unwrap.");

        return obj =>
        {
            var enumerable = (IEnumerable)obj;
            var collected = new List<object>();
            foreach (var item in enumerable)
            {
                if (item == null) continue;
                var primitive = valueProp.GetValue(item)!;
                var strongTyped = ctor != null
                    ? ctor.Invoke(new[] { primitive })
                    : fromMethod!.Invoke(null, new[] { primitive })!;
                collected.Add(strongTyped);
            }

            var array = Array.CreateInstance(strongIdType, collected.Count);
            for (var i = 0; i < collected.Count; i++) array.SetValue(collected[i], i);
            return array;
        };
    }

    // 9.0 (#4373): cache the closed-generic method invocation by
    // (targetType, methodName, openInterfaceType). Each LINQ filter / SELECT site that
    // currently hits CallGenericInterfaceMethod paid for GetMethod + MethodInfo.Invoke
    // (which boxes via object[]) per call. The cached compiled delegate takes the
    // target instance plus the single argument and returns the result.
    private readonly struct GenericMethodKey: IEquatable<GenericMethodKey>
    {
        public readonly Type TargetType;
        public readonly string MethodName;
        public readonly Type Interface;

        public GenericMethodKey(Type targetType, string methodName, Type @interface)
        {
            TargetType = targetType;
            MethodName = methodName;
            Interface = @interface;
        }

        public bool Equals(GenericMethodKey other) =>
            TargetType == other.TargetType
            && MethodName == other.MethodName
            && Interface == other.Interface;

        public override bool Equals(object? obj) => obj is GenericMethodKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(TargetType, MethodName, Interface);
    }

    private static readonly ConcurrentDictionary<GenericMethodKey, Func<object, object?, object?>> _callGenericMethodCache = new();

    public static object CallGenericInterfaceMethod(this object obj, Type openInterfaceType, string methodName, params object[] parameters)
    {
        // The hot call shape across IsOneOf.cs and SelectorVisitor.cs is exactly one
        // argument. Keep the params signature for compatibility but only cache the
        // single-arg shape; the rare zero/multi-arg cases fall through to plain
        // reflection. The cached delegate eliminates the per-call MethodInfo.Invoke
        // boxing of the single argument into an object[].
        if (parameters.Length != 1)
        {
            return obj.GetType().GetMethod(methodName)?.Invoke(obj, parameters)!;
        }

        var key = new GenericMethodKey(obj.GetType(), methodName, openInterfaceType);
        var invoker = _callGenericMethodCache.GetOrAdd(key, static k =>
        {
            var method = k.TargetType.GetMethod(k.MethodName)
                         ?? throw new InvalidOperationException(
                             $"Method '{k.MethodName}' not found on {k.TargetType.FullName}");

            var argParameters = method.GetParameters();
            var targetParam = Expression.Parameter(typeof(object), "target");
            var argParam = Expression.Parameter(typeof(object), "arg");
            var convertedTarget = Expression.Convert(targetParam, k.TargetType);
            var convertedArg = Expression.Convert(argParam, argParameters[0].ParameterType);
            var call = Expression.Call(convertedTarget, method, convertedArg);
            Expression body = method.ReturnType == typeof(void)
                ? Expression.Block(call, Expression.Constant(null, typeof(object)))
                : Expression.Convert(call, typeof(object));
            return Expression.Lambda<Func<object, object?, object?>>(body, targetParam, argParam).Compile();
        });

        return invoker(obj, parameters[0])!;
    }
}
