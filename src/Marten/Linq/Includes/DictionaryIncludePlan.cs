#nullable enable
using System;
using System.Collections.Generic;
using Marten.Internal.Storage;
using Marten.Linq.Members;

namespace Marten.Linq.Includes;

internal class DictionaryIncludePlan<T, TId>: IncludePlan<T> where T : notnull
{
    public DictionaryIncludePlan(IDocumentStorage<T> storage, IQueryableMember connectingMember,
        IDictionary<TId, T> dictionary): base(storage, connectingMember, BuildAction(storage, dictionary))
    {
    }

    public static Action<T> BuildAction(IDocumentStorage<T> storage, IDictionary<TId, T> dictionary)
    {
        void Callback(T item)
        {
            var id = (TId)storage.IdentityFor(item);
            dictionary[id] = item;
        }

        return Callback;
    }
}
