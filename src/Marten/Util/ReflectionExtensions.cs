using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Baseline;

namespace Marten.Util
{
    public static class ReflectionExtensions
    {
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

            if (member is FieldInfo)
            {
                rawType = member.As<FieldInfo>().FieldType;
            }
            if (member is PropertyInfo)
            {
                rawType = member.As<PropertyInfo>().PropertyType;
            }

            return rawType.IsNullable() ? rawType.GetInnerTypeFromNullable() : rawType;
        }

        public static string GetPrettyName(this Type t)
        {
            if (!t.IsGenericType)
                return t.Name;

            var sb = new StringBuilder();

            sb.Append(t.Name.Substring(0, t.Name.LastIndexOf("`", StringComparison.Ordinal)));
            sb.Append(t.GetGenericArguments().Aggregate("<", (aggregate, type) => aggregate + (aggregate == "<" ? "" : ",") + GetPrettyName(type)));
            sb.Append(">");

            return sb.ToString();
        }

        public static string GetTypeName(this Type type)
        {
            var typeName = type.Name;

            if (type.IsGenericType)
            {
                typeName = GetPrettyName(type);                
            }

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
    }
}