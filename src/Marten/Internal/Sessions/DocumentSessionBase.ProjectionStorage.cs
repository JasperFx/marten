using JasperFx.Events.Daemon;

namespace Marten.Internal.Sessions;

public abstract partial class DocumentSessionBase
{
    public IProjectionStorage<TDoc, TId> ProjectionStorageFor<TDoc, TId>(string tenantId)
    {
        throw new System.NotImplementedException();
    }

    public IProjectionStorage<TDoc, TId> ProjectionStorageFor<TDoc, TId>()
    {
        throw new System.NotImplementedException();
    }
}
