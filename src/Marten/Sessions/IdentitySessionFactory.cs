using Marten.Services;

namespace Marten.Sessions;

internal class IdentitySessionFactory: SessionFactoryBase
{
    public IdentitySessionFactory(IDocumentStore store) : base(store){}

    public override SessionOptions BuildOptions()
    {
        return new SessionOptions { Tracking = DocumentTracking.IdentityOnly };
    }
}
