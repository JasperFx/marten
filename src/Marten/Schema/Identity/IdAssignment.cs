using Marten.Storage;

namespace Marten.Schema.Identity
{
    public interface IdAssignment<T>
    {
        object Assign(ITenant tenant, T document, out bool assigned);

        void Assign(ITenant tenant, T document, object id);
    }
}