using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Marten.Linq.Members.ValueCollections;

/// <summary>
/// This takes the place of the ValueCollectionMember when this member
/// is used inside of a SelectMany() clause
/// </summary>
internal class SelectManyValueCollection: IValueCollectionMember
{
    public Type ElementType { get; }

    public SelectManyValueCollection(ValueCollectionMember valueCollectionMember, MemberInfo parentMember,
        Type elementType, StoreOptions options)
    {
        ElementType = elementType;
        var elementMember = new ElementMember(parentMember, elementType);
        var element = (QueryableMember)options.CreateQueryableMember(elementMember, valueCollectionMember, elementType);
        element.RawLocator = element.TypedLocator = "data";

        Element = element;
    }

    public SelectManyValueCollection(Type elementType, IQueryableMember parentMember, StoreOptions options)
    {
        ElementType = elementType;
        var elementMember = new ElementMember(typeof(ICollection<>).MakeGenericType(elementType), elementType);
        var element = (QueryableMember)options.CreateQueryableMember(elementMember, parentMember, elementType);
        element.RawLocator = element.TypedLocator = "data";

        Element = element;
    }

    public IQueryableMember Element { get; }

    public IQueryableMember FindMember(MemberInfo member)
    {
        return Element;
    }

    public void ReplaceMember(MemberInfo member, IQueryableMember queryableMember)
    {
        throw new NotSupportedException();
    }

    public IEnumerator<IQueryableMember> GetEnumerator()
    {
        return (IEnumerator<IQueryableMember>)new IQueryableMember[] { Element }.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
