using System;

namespace Marten.Linq.Members.Dictionaries;

internal interface IDictionaryMember : ICollectionMember
{
    IQueryableMember MemberForKey(object keyValue);

    Type ValueType { get; }
    Type KeyType { get; }
    DictionaryCountMember Count { get; }
}
