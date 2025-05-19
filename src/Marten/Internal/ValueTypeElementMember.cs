using System;
using Marten.Linq.Members.ValueCollections;

namespace Marten.Internal;

internal class ValueTypeElementMember: ElementMember
{
    public ValueTypeElementMember(Type declaringType, Type reflectedType) : base(declaringType, reflectedType)
    {
    }
}