using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Util
{
    internal static class ReflectionExtensions
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
                rawType = member.As<FieldInfo>().FieldType;
            if (member is PropertyInfo)
                rawType = member.As<PropertyInfo>().PropertyType;

            return rawType.IsNullable() ? rawType.GetInnerTypeFromNullable() : rawType;
        }

        public static Type GetRawMemberType(this MemberInfo member)
        {
            Type rawType = null;

            if (member is FieldInfo)
                rawType = member.As<FieldInfo>().FieldType;
            if (member is PropertyInfo)
                rawType = member.As<PropertyInfo>().PropertyType;

            return rawType;
        }

    }
}
