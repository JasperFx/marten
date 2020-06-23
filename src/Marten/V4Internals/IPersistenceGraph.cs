namespace Marten.V4Internals
{
    public interface IPersistenceGraph
    {
        DocumentPersistence<T> StorageFor<T>();
    }
}
