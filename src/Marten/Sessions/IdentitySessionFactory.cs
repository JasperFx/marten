namespace Marten.Sessions;

internal class IdentitySessionFactory: ISessionFactory
{
    private readonly IDocumentStore _store;

    public IdentitySessionFactory(IDocumentStore store) =>
        _store = store;

    public IQuerySession QuerySession() =>
        _store.QuerySession();

    public IDocumentSession OpenSession() =>
        _store.LightweightSession();
}
