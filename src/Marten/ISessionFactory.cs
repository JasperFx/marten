using System.Diagnostics;
using Marten.Services;

namespace Marten;

/// <summary>
///     Pluggable strategy for customizing how IDocumentSession / IQuerySession
///     objects are created within an application.
/// </summary>
public interface ISessionFactory
{
    /// <summary>
    ///     Build new instances of IQuerySession on demand
    /// </summary>
    /// <returns></returns>
    IQuerySession QuerySession();

    /// <summary>
    ///     Build new instances of IDocumentSession on demand
    /// </summary>
    /// <returns></returns>
    IDocumentSession OpenSession();
}

/// <summary>
/// Base class for simple creation of document sessions
/// </summary>
public abstract class SessionFactoryBase: ISessionFactory
{
    private readonly IDocumentStore _store;

    protected SessionFactoryBase(IDocumentStore store)
    {
        _store = store;
    }

    public abstract SessionOptions BuildOptions();

    public virtual IQuerySession QuerySession() =>
        _store.QuerySession();

    public IDocumentSession OpenSession()
    {
        var documentSession = _store.OpenSession(BuildOptions());

        documentSession.CorrelationId = Activity.Current?.RootId;
        documentSession.CausationId = Activity.Current?.ParentId;

        ApplyMetadata(documentSession);
        return documentSession;
    }

    /// <summary>
    /// A hook to apply tracking metadata to the Document Session
    /// </summary>
    /// <param name="documentSession"></param>
    public virtual void ApplyMetadata(IDocumentSession documentSession)
    {

    }
}
