namespace Marten.Sessions;

internal class DirtyTrackedSessionFactory: ISessionFactory
{
    private readonly IDocumentStore _store;

    public DirtyTrackedSessionFactory(IDocumentStore store) =>
        _store = store;

    public IQuerySession QuerySession() =>
        _store.QuerySession();

    public IDocumentSession OpenSession() =>
        _store.DirtyTrackedSession();
}
