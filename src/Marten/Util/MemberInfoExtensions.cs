using System;
using System.Reflection;
using FubuCore;

namespace Marten.Util
{
    public static class MemberInfoExtensions
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
    }
}