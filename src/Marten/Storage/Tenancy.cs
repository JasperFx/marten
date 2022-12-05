namespace Marten.Storage;

public abstract class Tenancy
{
    public const string DefaultTenantId = "*DEFAULT*";

    protected Tenancy(StoreOptions options)
    {
        Options = options;
    }

    internal StoreOptions Options { get; }
}
