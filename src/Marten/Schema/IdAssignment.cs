namespace Marten.Schema
{
    public interface IdAssignment<T>
    {
        void Assign(T document);
    }
}