using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Marten.Exceptions;

namespace Marten.Linq.Members.Dictionaries;

internal class KeyValuePairMemberCollection<TKey, TValue> : IQueryableMemberCollection
{
    private readonly Type _pairType;
    private readonly IQueryableMember _key;
    private readonly IQueryableMember _value;

    public KeyValuePairMemberCollection(StoreOptions options)
    {
        _pairType = typeof(KeyValuePair<TKey, TValue>);
        var root = new RootMember(typeof(IDictionary<TKey, TValue>));
        _key = options.CreateQueryableMember(_pairType.GetProperty("Key"), root, typeof(TKey));
        _value = options.CreateQueryableMember(_pairType.GetProperty("Value"), root, typeof(TValue));

        ElementType = _pairType;
    }

    public IQueryableMember FindMember(MemberInfo member)
    {
        if (member.Name == _key.MemberName) return _key;
        if (member.Name == _value.MemberName) return _value;

        throw new BadLinqExpressionException(
            "Marten does not support whatever in the world you just tried to do with querying through a Dictionary");
    }

    public void ReplaceMember(MemberInfo member, IQueryableMember queryableMember)
    {
        throw new NotSupportedException();
    }

    public IEnumerator<IQueryableMember> GetEnumerator()
    {
        throw new NotImplementedException();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public Type ElementType { get; }
}
