#nullable enable
using System;
using System.Reflection;

namespace Marten.Linq.Members.ValueCollections;

internal class ArrayIndexMember : MemberInfo
{
    public int Index { get; }

    public ArrayIndexMember(int index)
    {
        // #5949: PostgreSQL reads a negative jsonb array index from the end, so `x.Values[-1]` would quietly
        // become the last element. In C# it is out of range, so it is refused here rather than translated.
        if (index < 0)
        {
            throw new BadLinqExpressionException(
                $"Marten cannot translate the negative collection index {index}: PostgreSQL counts a negative index from the end of the array, C# does not");
        }

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
