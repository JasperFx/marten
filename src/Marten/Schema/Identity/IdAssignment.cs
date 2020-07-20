using System;
using Marten.Storage;

namespace Marten.Schema.Identity
{
    [Obsolete("goes away in v4")]
    public interface IdAssignment<T>
    {
        object Assign(ITenant tenant, T document, out bool assigned);

        void Assign(ITenant tenant, T document, object id);
    }
}
