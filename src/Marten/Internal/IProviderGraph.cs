using Marten.Internal.CodeGeneration;

namespace Marten.Internal
{
    public interface IProviderGraph
    {
        DocumentProvider<T> StorageFor<T>();
    }
}
