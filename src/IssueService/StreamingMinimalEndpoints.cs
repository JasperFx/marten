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

        // EmitETag = false opt-out
        app.MapGet("/minimal/issue/{id:guid}/no-etag",
            (Guid id, IQuerySession session)
                => new StreamOne<Issue>(session.Query<Issue>().Where(x => x.Id == id))
                {
                    EmitETag = false
                });

        // Document type whose version metadata is disabled — no mt_version column, so
        // EmitETag = true (the default) must still emit NO ETag rather than a constant zero-Guid.
        app.MapGet("/minimal/versionless/{id:guid}",
            (Guid id, IQuerySession session)
                => new StreamOne<VersionlessDoc>(session.Query<VersionlessDoc>().Where(x => x.Id == id)));

        // --- StreamMany<T> ---

        app.MapGet("/minimal/issues/open",
            (IQuerySession session)
                => new StreamMany<Issue>(session.Query<Issue>().Where(x => x.Open)));

        // Known-empty result — exercises the "no 404, empty array" contract
        app.MapGet("/minimal/issues/none",
            (IQuerySession session)
                => new StreamMany<Issue>(session.Query<Issue>().Where(x => x.Id == Guid.Empty)));

        // --- StreamPaged<T> ---

        app.MapGet("/minimal/issues/paged/{pageNumber:int}/{pageSize:int}",
            (int pageNumber, int pageSize, IQuerySession session)
                => new StreamPaged<Issue>(
                    session.Query<Issue>().Where(x => x.Open).OrderBy(x => x.Description),
                    pageNumber, pageSize));

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

        // --- StreamPagedByCursor<T> ---

        app.MapGet("/minimal/issues/paged-cursor",
            (IQuerySession session, int pageSize, string? cursor)
                => new StreamPagedByCursor<Issue>(
                    session.Query<Issue>().OrderBy(x => x.Description).ThenBy(x => x.Id),
                    cursor,
                    pageSize));

        // Mixed sort directions: descending primary key, ascending tie-breaker
        app.MapGet("/minimal/issues/paged-cursor-mixed",
            (IQuerySession session, int pageSize, string? cursor)
                => new StreamPagedByCursor<Issue>(
                    session.Query<Issue>().OrderByDescending(x => x.Description).ThenBy(x => x.Id),
                    cursor,
                    pageSize));

        // --- StreamEventState ---

        #region sample_minimal_api_stream_event_state

        app.MapGet("/minimal/order/{id:guid}/state",
            (Guid id, IQuerySession session)
                => new StreamEventState(session, id));

        #endregion

        app.MapGet("/minimal/named-order/{id}/state",
            (string id, IQuerySession session)
                => new StreamEventState(session, id));

        // --- StreamEvents ---

        #region sample_minimal_api_stream_events

        app.MapGet("/minimal/order/{id:guid}/events",
            (Guid id, IQuerySession session)
                => new StreamEvents(session, id));

        #endregion

        app.MapGet("/minimal/named-order/{id}/events",
            (string id, IQuerySession session)
                => new StreamEvents(session, id));

        #region sample_minimal_api_stream_events_from_version

        // Paging forward through a stream: running off the end is expected, not a 404
        app.MapGet("/minimal/order/{id:guid}/events/from/{fromVersion:long}",
            (Guid id, long fromVersion, IQuerySession session)
                => new StreamEvents(session, id, fromVersion: fromVersion)
                {
                    OnEmptyStatus = StatusCodes.Status200OK
                });

        #endregion

        // Version cap, and the plan-accepting constructor
        app.MapGet("/minimal/order/{id:guid}/events/upto/{version:long}",
            (Guid id, long version, IQuerySession session)
                => new StreamEvents(session, new FetchStreamPlan(id, version)));

        return app;
    }
}

/// <summary>
/// A document type registered with version metadata disabled (no <c>mt_version</c> column),
/// used to prove <see cref="StreamOne{T}"/> emits no ETag for versionless documents.
/// </summary>
public class VersionlessDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}
