namespace Marten.Sessions;

internal class LightweightSessionFactory: ISessionFactory
{
    private readonly IDocumentStore _store;

    public LightweightSessionFactory(IDocumentStore store) =>
        _store = store;

    public IQuerySession QuerySession() =>
        _store.QuerySession();

    public IDocumentSession OpenSession() =>
        _store.LightweightSession();
}
