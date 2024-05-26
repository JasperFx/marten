#nullable enable
using System.Collections.Generic;
using Marten.Internal.Storage;
using Marten.Linq.Members;

namespace Marten.Linq.Includes;

internal class ListIncludePlan<T>: IncludePlan<T> where T : notnull
{
    public ListIncludePlan(IDocumentStorage<T> storage, IQueryableMember connectingMember, IList<T> list): base(storage,
        connectingMember, list.Add)
    {
    }
}
