namespace Marten.Schema.Identity
{
    public interface IIdGenerator<T>
    {
        T Assign(T existing, out bool assigned);
    }
}