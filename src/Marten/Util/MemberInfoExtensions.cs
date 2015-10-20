using System;
using System.Reflection;
using FubuCore;

namespace Marten.Util
{
    public static class MemberInfoExtensions
    {
        public static Type GetMemberType(this MemberInfo member)
        {
            if (member is FieldInfo) return member.As<FieldInfo>().FieldType;
            if (member is PropertyInfo) return member.As<PropertyInfo>().PropertyType;

            throw new NotSupportedException();
        }
    }
}