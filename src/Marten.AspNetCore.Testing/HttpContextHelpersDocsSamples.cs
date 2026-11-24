using System;
using System.Linq;
using System.Threading.Tasks;
using IssueService.Controllers;
using Marten;
using Marten.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Marten.AspNetCore.Testing;

// Compiled samples for docs/documents/aspnetcore.md. Never run; they only have to compile.
public static class HttpContextHelpersDocsSamples
{
    public static void map_endpoints(WebApplication app)
    {
        #region sample_docs_write_paged_to_httpcontext

        app.MapGet("/issues/open/paged", (IQuerySession session, HttpContext context, int page, int pageSize) =>
            session.Query<Issue>()
                .Where(x => x.Open)
                .OrderBy(x => x.Description)
                // pageNumber is 1-based
                .WritePaged(context, page, pageSize));

        #endregion

        #region sample_docs_write_json_to_httpcontext

        app.MapGet("/issues/recent", (IQuerySession session, HttpContext context) =>
            // Every row has to be a single JSON value. The rows go out as one JSON array
            session.WriteJson(
                "select jsonb_build_object('id', d.id, 'lastModified', d.mt_last_modified) from mt_doc_issue as d where d.mt_last_modified > ?",
                context,
                parameters: [DateTimeOffset.UtcNow.AddDays(-1)]));

        #endregion
    }
}
