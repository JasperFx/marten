namespace Marten.Schema.Identity
{
    public interface IdAssignment<T>
    {
        object Assign(T document, out bool assigned);

        void Assign(T document, object id);
    }

    public interface IIdGeneration<T>
    {
        T Assign(T existing, out bool assigned);
    }

}