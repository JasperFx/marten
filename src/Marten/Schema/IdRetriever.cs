namespace Marten.Schema
{
    public interface IdRetriever<T>
    {
        object Retrieve(T document);
    }
}