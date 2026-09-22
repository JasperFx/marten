#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Marten.Exceptions;

namespace Marten.Linq.Members.Dictionaries;

internal class KeyValuePairMemberCollection<TKey, TValue> : IQueryableMemberCollection
{
    private readonly IQueryableMember _key;
    private readonly IQueryableMember _value;

    public KeyValuePairMemberCollection(StoreOptions options)
    {
        var pairType = typeof(KeyValuePair<TKey, TValue>);
        var root = new RootMember(typeof(IDictionary<TKey, TValue>));
        _key = options.CreateQueryableMember(pairType.GetProperty("Key")!, root, typeof(TKey));
        _value = options.CreateQueryableMember(pairType.GetProperty("Value")!, root, typeof(TValue));

        ElementType = pairType;
    }

    public IQueryableMember FindMember(MemberInfo member)
    {
        if (member.Name == _key.MemberName) return _key;
        if (member.Name == _value.MemberName) return _value;

        // #5481. This used to read "Marten does not support whatever in the world you just tried to
        // do with querying through a Dictionary", which is funny in a commit and useless in a
        // support ticket. Name the member that could not be resolved, the two that can be, and the
        // dictionary operations that do translate.
        throw new BadLinqExpressionException(
            $"Marten cannot translate the member '{member.DeclaringType?.Name}.{member.Name}' inside a Dictionary<{typeof(TKey).Name}, {typeof(TValue).Name}> sub-query: only '{_key.MemberName}' and '{_value.MemberName}' are addressable on the pair. Supported dictionary operations are ContainsKey(), Count, Any(), indexer equality (x.Dict[\"key\"] == value), Keys.Contains(), Values.Contains(), and Any(pair => ...) over simple comparisons of the pair's Key and Value. Use MatchesSql() with the PostgreSQL '#>' / '?' operators for anything else.");
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
