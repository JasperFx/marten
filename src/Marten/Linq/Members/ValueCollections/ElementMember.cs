using System;
using System.Reflection;

namespace Marten.Linq.Members.ValueCollections;

internal class ElementMember: MemberInfo
{
    public ElementMember(MemberInfo parent, Type elementType)
    {
        DeclaringType = parent.ReflectedType;
        ReflectedType = elementType;
    }

    public ElementMember(Type declaringType, Type reflectedType)
    {
        DeclaringType = declaringType;
        ReflectedType = reflectedType;
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

    public override Type DeclaringType { get; }
    public override MemberTypes MemberType => MemberTypes.Custom;
    public override string Name => "Element";
    public override Type ReflectedType { get; }
}
