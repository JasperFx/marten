using System;
using Marten.Storage;

namespace Marten.Schema.Identity
{
    [Obsolete("goes away in v4, but probably need to rewrite ViewProjection first")]
    public interface IdAssignment<T>
    {
        void Assign(ITenant tenant, T document, object id);
    }
}
