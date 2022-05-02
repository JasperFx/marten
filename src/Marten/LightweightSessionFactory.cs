namespace Marten
{
    internal class LightweightSessionFactory: ISessionFactory
    {
        private readonly IDocumentStore _store;

        public LightweightSessionFactory(IDocumentStore store)
        {
            _store = store;
        }

        public IQuerySession QuerySession()
        {
            return _store.QuerySession();
        }

        public IDocumentSession OpenSession()
        {
            return _store.LightweightSession();
        }
    }
}
