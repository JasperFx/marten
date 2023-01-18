using Marten.Exceptions;
using Microsoft.Extensions.Logging;

namespace Marten.Sessions;

#nullable enable
internal class DefaultSessionFactory: ISessionFactory
{
    private readonly IDocumentStore _store;
    private readonly ILogger<DefaultSessionFactory>? _logger;

    public DefaultSessionFactory(IDocumentStore store, ILogger<DefaultSessionFactory>? logger)
    {
        _store = store;
        _logger = logger;
    }

    public IQuerySession QuerySession() =>
        _store.QuerySession();

    public IDocumentSession OpenSession()
    {
        _logger?.LogWarning("""
            Opening a session without explicitly providing desired type may be dropped in next Marten version.
            For configuring Marten using the `AddMarten` method, specify explicitly the session type using
            `.AddMarten().UseLightweightSessions()` or `.AddMarten().UseIdentitySessions()` or `.AddMarten().UseDirtyTrackedSessions()`.
            We recommend using lightweight session by default. Read more in documentation: https://martendb.io/documents/sessions.html.
        """);

        return _store.IdentitySession();
    }
}
