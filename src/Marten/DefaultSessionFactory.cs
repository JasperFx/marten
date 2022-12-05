namespace Marten;

internal class DefaultSessionFactory: ISessionFactory
{
    private readonly IDocumentStore _store;

    public DefaultSessionFactory(IDocumentStore store)
    {
        _store = store;
    }

    public IQuerySession QuerySession()
    {
        return _store.QuerySession();
    }

    public IDocumentSession OpenSession()
    {
        return _store.OpenSession();
    }
}
