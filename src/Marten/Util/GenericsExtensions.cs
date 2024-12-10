using System;
using System.Linq;

namespace Marten.Util;

internal static class GenericsExtensions
{
    public static bool IsGenericInterfaceImplementation(this object obj, Type openInterfaceType)
    {
        return obj.GetType().GetInterfaces().Any(x => x.IsGenericType && openInterfaceType.IsAssignableFrom(x.GetGenericTypeDefinition()));
    }

    public static object CallGenericInterfaceMethod(this object obj, Type openInterfaceType, string methodName, params object[] parameters)
    {
        return obj.GetType().GetMethod(methodName)?.Invoke(obj, parameters);
    }

}
