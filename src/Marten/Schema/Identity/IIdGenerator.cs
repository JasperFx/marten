using System;
using Marten.Storage;

namespace Marten.Schema.Identity
{
    [Obsolete("Goes away in v4")]
    public interface IIdGenerator<T>
    {
        T Assign(ITenant tenant, T existing, out bool assigned);
    }
}
