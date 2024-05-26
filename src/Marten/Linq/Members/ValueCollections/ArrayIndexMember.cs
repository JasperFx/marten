#nullable enable
using System;
using System.Reflection;

namespace Marten.Linq.Members.ValueCollections;

internal class ArrayIndexMember : MemberInfo
{
    public int Index { get; }

    public ArrayIndexMember(int index)
    {
        Index = index;
    }

    public override object[] GetCustomAttributes(bool inherit)
    {
        return Array.Empty<object>();
    }

    public override object[] GetCustomAttributes(Type attributeType, bool inherit)
    {
        return Array.Empty<object>();
    }

    public override bool IsDefined(Type attributeType, bool inherit)
    {
        return false;
    }

    public override Type DeclaringType => typeof(Array);
    public override MemberTypes MemberType => MemberTypes.Custom;
    public override string Name => "ArrayIndex";
    public override Type ReflectedType => typeof(int);
}
