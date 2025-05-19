namespace Marten.Storage;

public abstract class Tenancy
{
    protected Tenancy(StoreOptions options)
    {
        Options = options;
    }

    internal StoreOptions Options { get; }
}
