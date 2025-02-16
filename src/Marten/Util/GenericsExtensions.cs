#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Marten.Util;

internal static class GenericsExtensions
{
    public static bool IsGenericInterfaceImplementation(this object obj, Type openInterfaceType)
    {
        return obj.GetType().GetInterfaces().Any(x => x.IsGenericType && openInterfaceType.IsAssignableFrom(x.GetGenericTypeDefinition()));
    }

    public static object UnwrapIEnumerableOfNullables(this object obj)
    {
        var type = obj.GetType();
        if (type.GetInterfaces().Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(IEnumerable<>) &&
                i.GetGenericArguments()[0].IsGenericType &&
                i.GetGenericArguments()[0].GetGenericTypeDefinition() == typeof(Nullable<>)))
        {
            // Get the underlying type of the Nullable<T>
            var strongIdtype = type.GetInterfaces()
                .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .GetGenericArguments()[0]
                .GetGenericArguments()[0];

            // Cast to IEnumerable<Nullable<T>> and filter for non-null values
            var unwrappedValues = ((IEnumerable)obj)
                .Cast<object>()
                .Where(x => x != null)
                .Select(x => x.GetType().GetProperty("Value").GetValue(x));

            // Create an array of the underlying type
            obj = Array.CreateInstance(strongIdtype, unwrappedValues.Count());
            var stronglyTypedValues = unwrappedValues.Select(x =>
            {
                var constructor = strongIdtype.GetConstructor(new[] { x.GetType() });
                object strongTypedId;
                if (constructor != null)
                {
                    strongTypedId = constructor.Invoke(new[] { x });
                }
                else
                {
                    // Use static builder method if no constructor is found. User can name the builder method anyway they want.
                    var fromMethod = strongIdtype.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m =>
                            m.ReturnType == strongIdtype &&
                            m.GetParameters().Length == 1 &&
                            m.GetParameters()[0].ParameterType == x.GetType()
                        );
                    if (fromMethod == null)
                    {
                        throw new InvalidOperationException($"Type {strongIdtype} does not have a constructor or a static builder method.");
                    }
                    strongTypedId = fromMethod.Invoke(null, new[] { x });
                }
                return strongTypedId;
            }).ToArray();
            Array.Copy(stronglyTypedValues.ToArray(), (Array)obj, unwrappedValues.Count());

            return obj;
        }

        return obj;
    }

    public static object CallGenericInterfaceMethod(this object obj, Type openInterfaceType, string methodName, params object[] parameters)
    {
        return obj.GetType().GetMethod(methodName)?.Invoke(obj, parameters)!;
    }

}
