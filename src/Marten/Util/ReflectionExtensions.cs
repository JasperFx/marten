using System;
using System.Reflection;
using Baseline;

namespace Marten.Util
{
    public static class ReflectionExtensions
    {
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

        public static string GetTypeName(this Type type)
        {
            return type.IsNested
                            ? $"{type.DeclaringType.Name}.{type.Name}"
                            : type.Name;
        }

        public static string GetTypeFullName(this Type type)
        {
            return type.IsNested
                            ? $"{type.DeclaringType.FullName}.{type.Name}"
                            : type.FullName;
        }
    }
}