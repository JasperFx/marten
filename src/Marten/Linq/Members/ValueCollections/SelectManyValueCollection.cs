#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Marten.Linq.Members.ValueCollections;

/// <summary>
/// This takes the place of the ValueCollectionMember when this member
/// is used inside of a SelectMany() clause
/// </summary>
public class SelectManyValueCollection: IValueCollectionMember
{
    private readonly StoreOptions _options;
    private readonly RootMember _root;
    public Type ElementType { get; }

    public SelectManyValueCollection(ValueCollectionMember valueCollectionMember, MemberInfo parentMember,
        Type elementType, StoreOptions options)
    {
        _options = options;

        ElementType = elementType;
        _root = new RootMember(ElementType) { Ancestors = Array.Empty<IQueryableMember>() };

        var elementMember = new ElementMember(parentMember, elementType);
        var element = (QueryableMember)options.CreateQueryableMember(elementMember, valueCollectionMember, elementType);
        element.RawLocator = element.TypedLocator = "data";

        Element = element;
    }

    public SelectManyValueCollection(Type elementType, IQueryableMember parentMember, StoreOptions options)
    {
        ElementType = elementType;
        _options = options;
        _root = new RootMember(ElementType) { Ancestors = Array.Empty<IQueryableMember>() };

        var elementMember = new ElementMember(typeof(ICollection<>).MakeGenericType(elementType), elementType);
        var element = (QueryableMember)options.CreateQueryableMember(elementMember, parentMember, elementType);
        element.RawLocator = element.TypedLocator = "data";

        Element = element;
    }

    public IQueryableMember Element { get; }

    public IQueryableMember FindMember(MemberInfo member)
    {
        if (member is ElementMember)
        {
            return Element;
        }

        return _options.CreateQueryableMember(member, _root);
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
