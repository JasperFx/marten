using System;
using System.Reflection;

namespace Marten.Linq.Parsing;

public sealed class IndexerKeyInfo(string key) : MemberInfo
{
    public override string Name { get; } = key;

    // Mandatory but not used
    public override Type DeclaringType => null!;
    public override Type ReflectedType => null!;
    public override MemberTypes MemberType => MemberTypes.Custom;
    public override object[] GetCustomAttributes(bool inherit) => [];
    public override object[] GetCustomAttributes(Type attributeType, bool inherit) => [];
    public override bool IsDefined(Type attributeType, bool inherit) => false;
}
