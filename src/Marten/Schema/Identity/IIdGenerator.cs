using Marten.Storage;

namespace Marten.Schema.Identity
{
    public interface IIdGenerator<T>
    {
        T Assign(ITenant tenant, T existing, out bool assigned);
    }
}