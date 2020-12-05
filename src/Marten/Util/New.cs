using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;

namespace Marten.Util
{
    public static class New<T>
    {
        public static readonly Func<T> Instance = Creator();

        private static Func<T> Creator()
        {
            var t = typeof(T);
            if (t == typeof(string))
                return Expression.Lambda<Func<T>>(Expression.Constant(string.Empty)).Compile();

            if (t.HasDefaultConstructor())
                return Expression.Lambda<Func<T>>(Expression.New(t)).Compile();

            return () => (T)FormatterServices.GetUninitializedObject(t);
        }
    }

    public static class New
    {
        public static bool HasDefaultConstructor(this Type t)
        {
            return t.IsValueType || t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null;
        }
    }

    [Obsolete("No longer used. Use Newtonsoft.Json.ConstructorHandling. Will be removed in version 4.")]
    public enum ConstructorHandling
    {
        DefaultPublic = 1,
        DefaultProtected = 2,
        DefaultPrivate = 4,
        NotInitialized = 8,
        AnyDefault = DefaultPublic | DefaultProtected | DefaultPrivate,
        All = AnyDefault | NotInitialized
    }
}
