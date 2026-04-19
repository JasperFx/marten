using System;
using System.Linq;
using IssueService.Controllers;
using Marten;
using Marten.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IssueService;

/// <summary>
/// Minimal-API endpoint registrations that exercise the
/// <see cref="StreamOne{T}"/>, <see cref="StreamMany{T}"/>, and
/// <see cref="StreamAggregate{T}"/> helpers. Used by the Marten.AspNetCore.Testing
/// Alba tests to prove the helpers work on bare Minimal API (no Wolverine.Http
/// code generation required).
/// </summary>
public static class StreamingMinimalEndpoints
{
    public static IEndpointRouteBuilder MapStreamingMinimalEndpoints(this IEndpointRouteBuilder app)
    {
        // --- StreamOne<T> ---

        app.MapGet("/minimal/issue/{id:guid}",
            (Guid id, IQuerySession session)
                => new StreamOne<Issue>(session.Query<Issue>().Where(x => x.Id == id)));

        // Custom OnFoundStatus (e.g., 202 Accepted to exercise the init property)
        app.MapGet("/minimal/issue/{id:guid}/accepted",
            (Guid id, IQuerySession session)
                => new StreamOne<Issue>(session.Query<Issue>().Where(x => x.Id == id))
                {
                    OnFoundStatus = StatusCodes.Status202Accepted
                });

        // Custom ContentType
        app.MapGet("/minimal/issue/{id:guid}/vendor-type",
            (Guid id, IQuerySession session)
                => new StreamOne<Issue>(session.Query<Issue>().Where(x => x.Id == id))
                {
                    ContentType = "application/vnd.marten.issue+json"
                });

        // --- StreamMany<T> ---

        app.MapGet("/minimal/issues/open",
            (IQuerySession session)
                => new StreamMany<Issue>(session.Query<Issue>().Where(x => x.Open)));

        // Known-empty result — exercises the "no 404, empty array" contract
        app.MapGet("/minimal/issues/none",
            (IQuerySession session)
                => new StreamMany<Issue>(session.Query<Issue>().Where(x => x.Id == Guid.Empty)));

        // --- StreamAggregate<T> ---

        app.MapGet("/minimal/order/{id:guid}",
            (Guid id, IDocumentSession session)
                => new StreamAggregate<Order>(session, id));

        app.MapGet("/minimal/named-order/{id}",
            (string id, IDocumentSession session)
                => new StreamAggregate<NamedOrder>(session, id));

        // --- StreamOne<TDoc, TOut> — compiled query ---

        app.MapGet("/minimal/compiled/issue/{id:guid}",
            (Guid id, IQuerySession session)
                => new StreamOne<Issue, Issue>(session, new IssueById { Id = id }));

        // Custom OnFoundStatus for the compiled single overload
        app.MapGet("/minimal/compiled/issue/{id:guid}/accepted",
            (Guid id, IQuerySession session)
                => new StreamOne<Issue, Issue>(session, new IssueById { Id = id })
                {
                    OnFoundStatus = StatusCodes.Status202Accepted
                });

        // --- StreamMany<TDoc, TOut> — compiled list query ---

        app.MapGet("/minimal/compiled/issues/open",
            (IQuerySession session)
                => new StreamMany<Issue, System.Collections.Generic.IEnumerable<Issue>>(
                    session, new OpenIssues()));

        return app;
    }
}
