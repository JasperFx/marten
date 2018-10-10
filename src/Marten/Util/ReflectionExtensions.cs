using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Util
{
    public static class ReflectionExtensions
    {
        internal static readonly Dictionary<Type, string> Aliases = new Dictionary<Type, string>
        {
            {typeof(int), "int"},
            {typeof(void), "void"},
            {typeof(string), "string"},
            {typeof(long), "long"},
            {typeof(double), "double"},
            {typeof(bool), "bool"},
            {typeof(Task), "Task"},
            {typeof(object), "object"},
            {typeof(object[]), "object[]"}
        };

        public static string ToTableAlias(this MemberInfo[] members)
        {
            return members.Select(x => x.ToTableAlias()).Join("_");
        }

        public static string ToTableAlias(this MemberInfo member)
        {
            return member.Name.ToTableAlias();
        }

        public static string ToTableAlias(this string name)
        {
            return name.SplitPascalCase().ToLower().Replace(" ", "_");
        }

        public static Type GetMemberType(this MemberInfo member)
        {
            Type rawType = null;

            if (member is FieldInfo) rawType = member.As<FieldInfo>().FieldType;
            if (member is PropertyInfo) rawType = member.As<PropertyInfo>().PropertyType;

            return rawType.IsNullable() ? rawType.GetInnerTypeFromNullable() : rawType;
        }

        public static string GetPrettyName(this Type t)
        {
            if (!t.GetTypeInfo().IsGenericType)
                return t.Name;

            var sb = new StringBuilder();

            sb.Append(t.Name.Substring(0, t.Name.LastIndexOf("`", StringComparison.Ordinal)));
            sb.Append(t.GetGenericArguments().Aggregate("<",
                (aggregate, type) => aggregate + (aggregate == "<" ? "" : ",") + GetPrettyName(type)));
            sb.Append(">");

            return sb.ToString();
        }

        public static string GetTypeName(this Type type)
        {
            var typeName = type.Name;

            if (type.GetTypeInfo().IsGenericType) typeName = GetPrettyName(type);

            return type.IsNested
                ? $"{type.DeclaringType.Name}.{typeName}"
                : typeName;
        }

        public static string GetTypeFullName(this Type type)
        {
            return type.IsNested
                ? $"{type.DeclaringType.FullName}.{type.Name}"
                : type.FullName;
        }

        public static bool IsGenericDictionary(this Type type)
        {
            return type.GetTypeInfo().IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>);
        }

        // http://stackoverflow.com/a/15273117/426840
        public static bool IsAnonymousType(this object instance)
        {
            if (instance == null)
                return false;

            return instance.GetType().Namespace == null;
        }


        /// <summary>
        ///     Derives the full type name *as it would appear in C# code*
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        internal static string FullNameInCode(this Type type)
        {
            if (Aliases.ContainsKey(type)) return Aliases[type];

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                var cleanName = type.Name.Split('`').First();
                if (type.IsNested) cleanName = $"{type.ReflectedType.NameInCode()}.{cleanName}";

                var args = type.GetGenericArguments().Select(x => x.FullNameInCode()).Join(", ");

                return $"{type.Namespace}.{cleanName}<{args}>";
            }

            if (type.FullName == null) return type.Name;

            return type.FullName.Replace("+", ".");
        }

        /// <summary>
        ///     Derives the type name *as it would appear in C# code*
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        internal static string NameInCode(this Type type)
        {
            if (Aliases.ContainsKey(type)) return Aliases[type];

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                var cleanName = type.Name.Split('`').First().Replace("+", ".");
                if (type.IsNested) cleanName = $"{type.ReflectedType.NameInCode()}.{cleanName}";

                var args = type.GetGenericArguments().Select(x => x.FullNameInCode()).Join(", ");

                return $"{cleanName}<{args}>";
            }

            if (type.MemberType == MemberTypes.NestedType) return $"{type.ReflectedType.NameInCode()}.{type.Name}";

            return type.Name.Replace("+", ".");
        }

        internal static string ShortNameInCode(this Type type)
        {
            if (Aliases.ContainsKey(type)) return Aliases[type];

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                var cleanName = type.Name.Split('`').First().Replace("+", ".");
                if (type.IsNested) cleanName = $"{type.ReflectedType.NameInCode()}.{cleanName}";

                var args = type.GetGenericArguments().Select(x => x.ShortNameInCode()).Join(", ");

                return $"{cleanName}<{args}>";
            }

            if (type.MemberType == MemberTypes.NestedType) return $"{type.ReflectedType.NameInCode()}.{type.Name}";

            return type.Name.Replace("+", ".");
        }
    }
}