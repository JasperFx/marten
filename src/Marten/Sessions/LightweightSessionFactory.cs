using Marten.Services;

namespace Marten.Sessions;

internal class LightweightSessionFactory: SessionFactoryBase
{
    public LightweightSessionFactory(IDocumentStore store) : base(store){}

    public override SessionOptions BuildOptions()
    {
        return new SessionOptions { Tracking = DocumentTracking.None };
    }
}
