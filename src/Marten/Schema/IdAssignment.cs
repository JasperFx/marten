namespace Marten.Schema
{
    public interface IdAssignment<T>
    {
        object Assign(T document, out bool assigned);

        void Assign(T document, object id);
    }
}