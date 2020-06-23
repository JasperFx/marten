namespace Marten.V4Internals.Sessions
{
    public class LightweightSession: NewDocumentSession
    {
        public LightweightSession(IDocumentStore store, IDatabase database, ISerializer serializer, ITenant tenant, IPersistenceGraph persistence, StoreOptions options) : base(store, database, serializer, tenant, persistence, options)
        {
        }

        protected override IDocumentStorage<T> selectStorage<T>(DocumentPersistence<T> persistence)
        {
            return persistence.Lightweight;
        }
    }
}
