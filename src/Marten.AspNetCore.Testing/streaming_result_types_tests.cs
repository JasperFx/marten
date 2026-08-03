using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alba;
using IssueService;
using IssueService.Controllers;
using Marten.AspNetCore;
using Marten.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Marten.AspNetCore.Testing;

/// <summary>
/// Alba-based tests for <see cref="StreamOne{T}"/>, <see cref="StreamMany{T}"/>,
/// and <see cref="StreamAggregate{T}"/> executing against plain Minimal API
/// endpoints (no Wolverine required).
/// </summary>
[Collection("integration")]
public class streaming_result_types_tests: IntegrationContext
{
    private readonly IAlbaHost theHost;

    public streaming_result_types_tests(AppFixture fixture) : base(fixture)
    {
        theHost = fixture.Host;
    }

    // ───────────────────────── StreamOne<T> ─────────────────────────

    [Fact]
    public async Task stream_one_returns_matching_document_as_json()
    {
        var issue = new Issue { Description = "stream_one hit", Open = true };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issue/{issue.Id}");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var read = result.ReadAsJson<Issue>();
        read.Description.ShouldBe(issue.Description);
    }

    [Fact]
    public async Task stream_one_sets_content_length_on_hit()
    {
        var issue = new Issue { Description = "has-length", Open = false };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issue/{issue.Id}");
            s.StatusCodeShouldBe(200);
        });

        // Marten.AspNetCore's WriteSingle buffers the document and sets Content-Length.
        result.Context.Response.ContentLength.HasValue.ShouldBeTrue();
    }

    [Fact]
    public async Task stream_one_returns_404_when_no_match()
    {
        await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issue/{Guid.NewGuid()}");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task stream_one_respects_custom_on_found_status()
    {
        var issue = new Issue { Description = "accepted", Open = true };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issue/{issue.Id}/accepted");
            s.StatusCodeShouldBe(202);
            s.ContentTypeShouldBe("application/json");
        });
    }

    [Fact]
    public async Task stream_one_respects_custom_content_type()
    {
        var issue = new Issue { Description = "vendor", Open = true };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issue/{issue.Id}/vendor-type");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/vnd.marten.issue+json");
        });
    }

    // ───────────────────────── StreamOne<T> ETag / If-None-Match ─────────────────────────

    [Fact]
    public async Task stream_one_sets_etag_header_on_hit()
    {
        var issue = new Issue { Description = "etag hit", Open = true };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issue/{issue.Id}");
            s.StatusCodeShouldBe(200);
        });

        result.Context.Response.Headers.ContainsKey("ETag").ShouldBeTrue();
        result.Context.Response.Headers["ETag"].ToString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task stream_one_returns_304_when_if_none_match_matches_current_version()
    {
        var issue = new Issue { Description = "etag 304", Open = true };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        var first = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issue/{issue.Id}");
            s.StatusCodeShouldBe(200);
        });

        var etag = first.Context.Response.Headers["ETag"].ToString();
        etag.ShouldNotBeNullOrEmpty();

        var second = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issue/{issue.Id}");
            s.WithRequestHeader("If-None-Match", etag);
            s.StatusCodeShouldBe(304);
        });

        second.Context.Response.Headers["ETag"].ToString().ShouldBe(etag);
        second.ReadAsText().ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task stream_one_returns_full_body_when_if_none_match_is_stale()
    {
        var issue = new Issue { Description = "etag stale", Open = true };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issue/{issue.Id}");
            s.WithRequestHeader("If-None-Match", "\"" + Guid.NewGuid().ToString("D") + "\"");
            s.StatusCodeShouldBe(200);
        });

        var read = result.ReadAsJson<Issue>();
        read.Description.ShouldBe(issue.Description);
    }

    [Fact]
    public async Task stream_one_suppresses_etag_when_emit_etag_is_false()
    {
        var issue = new Issue { Description = "no etag", Open = true };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issue/{issue.Id}/no-etag");
            s.StatusCodeShouldBe(200);
        });

        result.Context.Response.Headers.ContainsKey("ETag").ShouldBeFalse();
    }

    [Fact]
    public async Task stream_one_emits_no_etag_when_version_metadata_disabled()
    {
        // VersionlessDoc is registered with Metadata.Version.Enabled = false and carries no
        // numeric revision metadata either (not a projection target, not IRevisioned), so there
        // is no mt_version column of either flavor to derive an ETag from. EmitETag defaults to
        // true, but the inline version read comes back null and no ETag (and no false 304) is
        // produced.
        var doc = new VersionlessDoc { Id = Guid.NewGuid(), Name = "no version column" };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/versionless/{doc.Id}");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        result.Context.Response.Headers.ContainsKey("ETag").ShouldBeFalse();

        var read = result.ReadAsJson<VersionlessDoc>();
        read.Name.ShouldBe(doc.Name);
    }

    [Fact]
    public async Task stream_one_returns_404_without_etag_when_no_match()
    {
        // The 404 path must not leak an ETag header even with EmitETag on.
        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issue/{Guid.NewGuid()}");
            s.StatusCodeShouldBe(404);
        });

        result.Context.Response.Headers.ContainsKey("ETag").ShouldBeFalse();
    }

    [Fact]
    public async Task stream_one_with_etag_executes_a_single_db_command()
    {
        // Acceptance for #5027: with EmitETag = true (default), the document JSON and its
        // mt_version come back in ONE round trip — no follow-up MetadataForAsync query.
        var store = theHost.Services.GetRequiredService<IDocumentStore>();

        var issue = new Issue { Description = "single command", Open = true };
        await using (var session = store.LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var logger = new CommandCountingLogger();
        query.Logger = logger;

        // Warm up storage-existence checks on this session so they don't count against us,
        // then reset the counter to isolate the WriteSingle round trips.
        await query.Query<Issue>().Where(x => x.Id == Guid.NewGuid())
            .StreamJsonFirstOrDefault(new MemoryStream());
        logger.Count = 0;

        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await query.Query<Issue>().Where(x => x.Id == issue.Id)
            .WriteSingle(context, emitETag: true);

        context.Response.StatusCode.ShouldBe(200);
        context.Response.Headers.ContainsKey("ETag").ShouldBeTrue();
        logger.Count.ShouldBe(1);
    }

    /// <summary>Minimal session logger that counts executed commands for the round-trip acceptance test.</summary>
    private sealed class CommandCountingLogger: Marten.IMartenSessionLogger
    {
        public int Count { get; set; }

        public void LogSuccess(Npgsql.NpgsqlCommand command) { }
        public void LogFailure(Npgsql.NpgsqlCommand command, Exception ex) { }
        public void LogSuccess(Npgsql.NpgsqlBatch batch) { }
        public void LogFailure(Npgsql.NpgsqlBatch batch, Exception ex) { }
        public void LogFailure(Exception ex, string message) { }
        public void RecordSavedChanges(Marten.IDocumentSession session, Marten.Services.IChangeSet commit) { }
        public void OnBeforeExecute(Npgsql.NpgsqlCommand command) => Count++;
        public void OnBeforeExecute(Npgsql.NpgsqlBatch batch) => Count++;
    }

    // ──────────────── StreamOne<T> ETag — numeric-revision documents ────────────────

    [Fact]
    public async Task stream_one_emits_stream_version_etag_for_projection_target_document()
    {
        // Order is the target of an inline single-stream projection (Projections.Snapshot<Order>),
        // so ProjectionDocumentPolicy forces numeric revisions and the projection writes the source
        // stream's version into mt_version. Serving the projected document through StreamOne emits
        // that revision as the ETag — the same value StreamAggregate derives for the same stream.
        var orderId = Guid.NewGuid();
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Events.StartStream<Order>(orderId, new OrderPlaced("Projected Book", 12.50m));
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order-doc/{orderId}");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        result.Context.Response.Headers["ETag"].ToString().ShouldBe("\"1\"");

        var order = result.ReadAsJson<Order>();
        order.Id.ShouldBe(orderId);
        order.Description.ShouldBe("Projected Book");
    }

    [Fact]
    public async Task stream_one_returns_304_when_if_none_match_matches_projection_revision()
    {
        var orderId = Guid.NewGuid();
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Events.StartStream<Order>(orderId, new OrderPlaced("Cached Projection", 3.00m));
            await session.SaveChangesAsync();
        }

        var second = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order-doc/{orderId}");
            s.WithRequestHeader("If-None-Match", "\"1\"");
            s.StatusCodeShouldBe(304);
        });

        second.Context.Response.Headers["ETag"].ToString().ShouldBe("\"1\"");
        second.ReadAsText().ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task stream_one_projection_etag_changes_when_the_stream_advances()
    {
        var orderId = Guid.NewGuid();
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Events.StartStream<Order>(orderId, new OrderPlaced("Evolving Book", 8.00m));
            await session.SaveChangesAsync();
        }

        var first = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order-doc/{orderId}");
            s.StatusCodeShouldBe(200);
        });
        first.Context.Response.Headers["ETag"].ToString().ShouldBe("\"1\"");

        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Events.Append(orderId, new OrderShipped());
            await session.SaveChangesAsync();
        }

        // The previously-cached ETag is now stale: full body again, with the new revision.
        var second = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order-doc/{orderId}");
            s.WithRequestHeader("If-None-Match", "\"1\"");
            s.StatusCodeShouldBe(200);
        });

        second.Context.Response.Headers["ETag"].ToString().ShouldBe("\"2\"");

        var order = second.ReadAsJson<Order>();
        order.Shipped.ShouldBeTrue();
    }

    [Fact]
    public async Task stream_one_emits_revision_etag_for_plain_revisioned_document()
    {
        // Not a projection target: RevisionedIssueNote opts into numeric revisions by
        // implementing IRevisioned, which also gives its mt_version column the narrower
        // integer width (#4614) — proving the revision read handles both column widths.
        var note = new RevisionedIssueNote { Id = Guid.NewGuid(), Name = "rev one" };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(note);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/revisioned/{note.Id}");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        result.Context.Response.Headers["ETag"].ToString().ShouldBe("\"1\"");

        await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/revisioned/{note.Id}");
            s.WithRequestHeader("If-None-Match", "\"1\"");
            s.StatusCodeShouldBe(304);
        });
    }

    [Fact]
    public async Task stream_one_emits_revision_etag_for_long_versioned_document()
    {
        // ILongVersioned keeps the default bigint mt_version column, where IRevisioned narrows it
        // to integer (#4614). Pinning both proves the revision read copes with either width, and
        // that a multi-stream-shaped target emits its own per-document counter as a valid ETag.
        var note = new LongVersionedIssueNote { Id = Guid.NewGuid(), Name = "long rev" };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(note);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/long-versioned/{note.Id}");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        result.Context.Response.Headers["ETag"].ToString().ShouldBe("\"1\"");

        await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/long-versioned/{note.Id}");
            s.WithRequestHeader("If-None-Match", "\"1\"");
            s.StatusCodeShouldBe(304);
        });
    }

    [Fact]
    public async Task stream_one_returns_404_without_etag_for_a_revisioned_document()
    {
        // The 404 branch runs before any ETag is formatted, but that was only pinned on the Guid
        // path — a projection-target miss must not leak a header either.
        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order-doc/{Guid.NewGuid()}");
            s.StatusCodeShouldBe(404);
        });

        result.Context.Response.Headers.ContainsKey("ETag").ShouldBeFalse();
    }

    [Fact]
    public async Task stream_one_suppresses_etag_on_a_revisioned_document_when_emit_etag_is_false()
    {
        var note = new RevisionedIssueNote { Id = Guid.NewGuid(), Name = "opted out" };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(note);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/revisioned/{note.Id}/no-etag");
            s.StatusCodeShouldBe(200);
        });

        result.Context.Response.Headers.ContainsKey("ETag").ShouldBeFalse();

        // The opt-out must still serve the document, not just drop the header.
        result.ReadAsJson<RevisionedIssueNote>().Name.ShouldBe("opted out");
    }

    [Fact]
    public async Task stream_one_emits_a_guid_etag_for_an_event_projection_output_document()
    {
        // ProjectionDocumentPolicy only forces numeric revisions onto *aggregate* projection
        // targets. An EventProjection's output keeps the plain-document default, which is Guid
        // version metadata — so it does emit an ETag, just not a stream-derived one. That ETag
        // is opaque and changes on every projection write, so it is only safe as a cache
        // validator, never as a stream-version equivalent.
        var orderId = Guid.NewGuid();
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Events.StartStream<Order>(orderId, new OrderPlaced("touched", 5.00m));
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/event-projection-doc/{orderId}");
            s.StatusCodeShouldBe(200);
        });

        var etag = result.Context.Response.Headers["ETag"].ToString();
        etag.ShouldNotBeNullOrEmpty();

        // A Guid ETag, not the stream version the aggregate target would have served.
        Guid.TryParse(etag.Trim('"'), out _).ShouldBeTrue();
        etag.ShouldNotBe("\"1\"");

        await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/event-projection-doc/{orderId}");
            s.WithRequestHeader("If-None-Match", etag);
            s.StatusCodeShouldBe(304);
        });
    }

    [Fact]
    public async Task stream_one_with_revision_etag_executes_a_single_db_command()
    {
        // Companion to stream_one_with_etag_executes_a_single_db_command, which only covered the
        // Guid flavor. The revision flavor is the projection read-model path — the hot one — so
        // pin that it also resolves the document AND its ETag in ONE round trip.
        var store = theHost.Services.GetRequiredService<IDocumentStore>();

        var orderId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<Order>(orderId, new OrderPlaced("single command", 1.00m));
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var logger = new CommandCountingLogger();
        query.Logger = logger;

        // Warm up storage-existence checks on this session so they don't count against us.
        await query.Query<Order>().Where(x => x.Id == Guid.NewGuid())
            .StreamJsonFirstOrDefault(new MemoryStream());
        logger.Count = 0;

        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await query.Query<Order>().Where(x => x.Id == orderId)
            .WriteSingle(context, emitETag: true);

        context.Response.StatusCode.ShouldBe(200);
        context.Response.Headers["ETag"].ToString().ShouldBe("\"1\"");
        logger.Count.ShouldBe(1);
    }

    [Fact]
    public async Task stream_one_does_not_buffer_the_document_body_on_a_304()
    {
        // A conditional-request hit reads the version off the row and then declines the payload,
        // so nothing is copied into the response buffer. The read itself still happens — the row
        // comes back either way — but the document copy does not.
        var store = theHost.Services.GetRequiredService<IDocumentStore>();

        var orderId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<Order>(orderId, new OrderPlaced(new string('x', 20_000), 1.00m));
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();

        var body = new MemoryStream();
        var context = new DefaultHttpContext { Response = { Body = body } };
        context.Request.Headers["If-None-Match"] = "\"1\"";

        await query.Query<Order>().Where(x => x.Id == orderId).WriteSingle(context, emitETag: true);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status304NotModified);
        context.Response.Headers["ETag"].ToString().ShouldBe("\"1\"");
        context.Response.ContentLength.ShouldBe(0);
        body.Length.ShouldBe(0);
    }

    [Fact]
    public async Task stream_one_and_stream_aggregate_serve_the_same_etag_for_the_same_stream()
    {
        // The cache-coherence claim #5120 rests on, and the reason serving a read model through
        // StreamOne rather than StreamAggregate is safe: for an Inline SingleStreamProjection the
        // document's revision IS the source stream's version, so a client can switch between the
        // two read styles without invalidating what it has cached. Asserted rather than assumed —
        // both endpoints are hit for the same stream and their ETags compared, at two different
        // stream versions so a coincidental match at "1" cannot pass.
        var orderId = Guid.NewGuid();
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Events.StartStream<Order>(orderId, new OrderPlaced("coherent", 9.00m));
            await session.SaveChangesAsync();
        }

        var one = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order-doc/{orderId}");
            s.StatusCodeShouldBe(200);
        });
        var aggregate = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}");
            s.StatusCodeShouldBe(200);
        });

        var etag = one.Context.Response.Headers["ETag"].ToString();
        etag.ShouldBe("\"1\"");
        aggregate.Context.Response.Headers["ETag"].ToString().ShouldBe(etag);

        // Advance the stream and confirm the two stay in step rather than agreeing only at 1.
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Events.Append(orderId, new OrderShipped());
            await session.SaveChangesAsync();
        }

        var oneAgain = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order-doc/{orderId}");
            s.StatusCodeShouldBe(200);
        });
        var aggregateAgain = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}");
            s.StatusCodeShouldBe(200);
        });

        oneAgain.Context.Response.Headers["ETag"].ToString().ShouldBe("\"2\"");
        aggregateAgain.Context.Response.Headers["ETag"].ToString().ShouldBe("\"2\"");

        // And a tag minted by one style is honored by the other.
        await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}");
            s.WithRequestHeader("If-None-Match", oneAgain.Context.Response.Headers["ETag"].ToString());
            s.StatusCodeShouldBe(304);
        });
        await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order-doc/{orderId}");
            s.WithRequestHeader("If-None-Match", aggregateAgain.Context.Response.Headers["ETag"].ToString());
            s.StatusCodeShouldBe(304);
        });
    }

    // ──────────────── StreamOne<T> ETag — Select() projections (#5158) ────────────────

    [Fact]
    public async Task stream_one_over_a_select_projection_serves_the_projection_and_the_document_etag()
    {
        // #5158: VersionSelectClause rebuilds the select list from the inner clause's SelectFields()
        // rather than delegating to its Apply, which dropped the `as data` alias that
        // SelectDataSelectClause emits — so the reader's GetOrdinal("data") threw
        // IndexOutOfRangeException. The plain path only worked because its field is the literal
        // `d.data`, which Postgres happens to name `data`.
        var issue = new Issue { Description = "projected", Open = true };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        var full = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issue/{issue.Id}");
            s.StatusCodeShouldBe(200);
        });
        var etag = full.Context.Response.Headers["ETag"].ToString();
        etag.ShouldNotBeNullOrEmpty();

        var projected = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issue/{issue.Id}/summary");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        projected.ReadAsJson<IssueSummary>().Description.ShouldBe("projected");

        // The projection is a pure function of the document, so it validates against the same
        // version — a client can cache either representation off the same ETag.
        projected.Context.Response.Headers["ETag"].ToString().ShouldBe(etag);

        var cached = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issue/{issue.Id}/summary");
            s.WithRequestHeader("If-None-Match", etag);
            s.StatusCodeShouldBe(304);
        });
        cached.ReadAsText().ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task stream_one_over_an_anonymous_type_projection_emits_the_document_etag()
    {
        // The anonymous-type shape from the #5158 report. It cannot go through an endpoint (the
        // result type has to be nameable), so it is exercised against WriteSingle directly.
        var store = theHost.Services.GetRequiredService<IDocumentStore>();
        var issue = new Issue { Description = "anonymous", Open = true };
        await using (var session = store.LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await query.Query<Issue>().Where(x => x.Id == issue.Id)
            .Select(x => new { x.Description })
            .WriteSingle(context, emitETag: true);

        context.Response.StatusCode.ShouldBe(200);
        context.Response.Headers["ETag"].ToString().ShouldNotBeNullOrEmpty();

        context.Response.Body.Position = 0;
        (await new StreamReader(context.Response.Body).ReadToEndAsync())
            .ShouldContain("anonymous");
    }

    [Fact]
    public async Task stream_one_over_a_scalar_projection_emits_the_document_etag()
    {
        // The second #5158 failure mode: for a scalar projection T is a primitive, and looking up
        // a document mapping for it threw ArgumentOutOfRangeException ("This type cannot be used as
        // a Marten document") before any SQL was even built. The flavor now comes from the source
        // document type instead.
        var store = theHost.Services.GetRequiredService<IDocumentStore>();
        var issue = new Issue { Description = "scalar projection", Open = true };
        await using (var session = store.LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await query.Query<Issue>().Where(x => x.Id == issue.Id)
            .Select(x => x.Description)
            .WriteSingle(context, emitETag: true);

        context.Response.StatusCode.ShouldBe(200);
        context.Response.Headers["ETag"].ToString().ShouldNotBeNullOrEmpty();

        context.Response.Body.Position = 0;
        (await new StreamReader(context.Response.Body).ReadToEndAsync())
            .ShouldBe("\"scalar projection\"");
    }

    [Fact]
    public async Task stream_one_over_a_select_projection_of_a_revisioned_document_emits_the_revision()
    {
        // The projection path on the numeric-revision flavor: the ETag is still the source
        // document's revision, which for a single-stream projection target is the stream version.
        var store = theHost.Services.GetRequiredService<IDocumentStore>();
        var orderId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<Order>(orderId, new OrderPlaced("projected order", 4.00m));
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await query.Query<Order>().Where(x => x.Id == orderId)
            .Select(x => new { x.Description })
            .WriteSingle(context, emitETag: true);

        context.Response.StatusCode.ShouldBe(200);
        context.Response.Headers["ETag"].ToString().ShouldBe("\"1\"");
    }

    [Fact]
    public async Task stream_one_over_a_select_projection_of_a_versionless_document_emits_no_etag()
    {
        // The source document type decides the flavor, so a projection over a type with no
        // mt_version column must still emit no ETag rather than picking up a default from the
        // projected type.
        var store = theHost.Services.GetRequiredService<IDocumentStore>();
        var doc = new VersionlessDoc { Id = Guid.NewGuid(), Name = "no version" };
        await using (var session = store.LightweightSession())
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await query.Query<VersionlessDoc>().Where(x => x.Id == doc.Id)
            .Select(x => new { x.Name })
            .WriteSingle(context, emitETag: true);

        context.Response.StatusCode.ShouldBe(200);
        context.Response.Headers.ContainsKey("ETag").ShouldBeFalse();
    }

    // ───────────────────────── StreamMany<T> ─────────────────────────

    [Fact]
    public async Task stream_many_returns_json_array()
    {
        // Seed three open issues with a unique description prefix to assert against
        var prefix = "many_" + Guid.NewGuid().ToString("N")[..8];
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(new Issue { Description = prefix + "_a", Open = true });
            session.Store(new Issue { Description = prefix + "_b", Open = true });
            session.Store(new Issue { Description = prefix + "_c", Open = true });
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url("/minimal/issues/open");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var body = result.ReadAsJson<List<Issue>>();
        body.Count(x => x.Description.StartsWith(prefix)).ShouldBe(3);
    }

    [Fact]
    public async Task stream_many_returns_empty_array_when_no_match_not_404()
    {
        var result = await theHost.Scenario(s =>
        {
            s.Get.Url("/minimal/issues/none");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        result.ReadAsText().Trim().ShouldBe("[]");
    }

    // ───────────────────── StreamAggregate<T> ─────────────────────

    [Fact]
    public async Task stream_aggregate_returns_latest_aggregate_as_json()
    {
        var orderId = Guid.NewGuid();
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Events.StartStream<Order>(orderId, new OrderPlaced("Book", 19.99m));
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var order = result.ReadAsJson<Order>();
        order.Id.ShouldBe(orderId);
        order.Description.ShouldBe("Book");
        order.Amount.ShouldBe(19.99m);
    }

    [Fact]
    public async Task stream_aggregate_returns_404_for_unknown_id()
    {
        await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{Guid.NewGuid()}");
            s.StatusCodeShouldBe(404);
        });
    }

    // ───────────── StreamAggregate<T> ETag / If-None-Match ─────────────

    [Fact]
    public async Task stream_aggregate_sets_etag_header_on_hit()
    {
        var orderId = Guid.NewGuid();
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Events.StartStream<Order>(orderId, new OrderPlaced("Etag Book", 9.99m));
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}");
            s.StatusCodeShouldBe(200);
        });

        result.Context.Response.Headers["ETag"].ToString().ShouldBe("\"1\"");
    }

    [Fact]
    public async Task stream_aggregate_returns_304_when_if_none_match_matches_stream_version()
    {
        var orderId = Guid.NewGuid();
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Events.StartStream<Order>(orderId, new OrderPlaced("Cached Book", 5.00m));
            await session.SaveChangesAsync();
        }

        var second = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}");
            s.WithRequestHeader("If-None-Match", "\"1\"");
            s.StatusCodeShouldBe(304);
        });

        second.Context.Response.Headers["ETag"].ToString().ShouldBe("\"1\"");
        second.ReadAsText().ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task stream_aggregate_returns_full_body_when_if_none_match_is_stale()
    {
        var orderId = Guid.NewGuid();
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Events.StartStream<Order>(orderId, new OrderPlaced("Fresh Book", 15.00m));
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}");
            s.WithRequestHeader("If-None-Match", "\"999\"");
            s.StatusCodeShouldBe(200);
        });

        var order = result.ReadAsJson<Order>();
        order.Description.ShouldBe("Fresh Book");
    }

    // ───────────────────────── OpenAPI metadata ─────────────────────────

    [Fact]
    public void stream_one_endpoint_advertises_produces_T_and_404_in_metadata()
    {
        var metadata = EndpointMetadataFor("GET", "/minimal/issue/{id:guid}");

        metadata.OfType<IProducesResponseTypeMetadata>()
            .ShouldContain(m => m.StatusCode == 200 && m.Type == typeof(Issue));
        metadata.OfType<IProducesResponseTypeMetadata>()
            .ShouldContain(m => m.StatusCode == 404);
    }

    [Fact]
    public void stream_many_endpoint_advertises_produces_array_in_metadata()
    {
        var metadata = EndpointMetadataFor("GET", "/minimal/issues/open");

        metadata.OfType<IProducesResponseTypeMetadata>()
            .ShouldContain(m => m.StatusCode == 200 && m.Type == typeof(IReadOnlyList<Issue>));
    }

    [Fact]
    public void stream_aggregate_endpoint_advertises_produces_T_and_404_in_metadata()
    {
        var metadata = EndpointMetadataFor("GET", "/minimal/order/{id:guid}");

        metadata.OfType<IProducesResponseTypeMetadata>()
            .ShouldContain(m => m.StatusCode == 200 && m.Type == typeof(Order));
        metadata.OfType<IProducesResponseTypeMetadata>()
            .ShouldContain(m => m.StatusCode == 404);
    }

    // ───────────────── StreamOne<TDoc, TOut> compiled query ─────────────────

    [Fact]
    public async Task compiled_stream_one_returns_matching_document_as_json()
    {
        var issue = new Issue { Description = "compiled stream_one hit", Open = true };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/compiled/issue/{issue.Id}");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var read = result.ReadAsJson<Issue>();
        read.Description.ShouldBe(issue.Description);
    }

    [Fact]
    public async Task compiled_stream_one_returns_404_when_no_match()
    {
        await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/compiled/issue/{Guid.NewGuid()}");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task compiled_stream_one_honours_custom_onfound_status()
    {
        var issue = new Issue { Description = "compiled custom-status", Open = true };
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/compiled/issue/{issue.Id}/accepted");
            s.StatusCodeShouldBe(202);
        });
    }

    [Fact]
    public void compiled_stream_one_endpoint_advertises_produces_T_and_404_in_metadata()
    {
        var metadata = EndpointMetadataFor("GET", "/minimal/compiled/issue/{id:guid}");

        metadata.OfType<IProducesResponseTypeMetadata>()
            .ShouldContain(m => m.StatusCode == 200 && m.Type == typeof(Issue));
        metadata.OfType<IProducesResponseTypeMetadata>()
            .ShouldContain(m => m.StatusCode == 404);
    }

    // ──────────────── StreamMany<TDoc, TOut> compiled list query ────────────────

    [Fact]
    public async Task compiled_stream_many_returns_json_array()
    {
        await using (var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            session.Store(new Issue { Description = "compiled-open-1", Open = true });
            session.Store(new Issue { Description = "compiled-open-2", Open = true });
            session.Store(new Issue { Description = "compiled-closed", Open = false });
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url("/minimal/compiled/issues/open");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var read = result.ReadAsJson<List<Issue>>();
        read.ShouldNotBeNull();
        read.ShouldAllBe(x => x.Open);
    }

    [Fact]
    public void compiled_stream_many_endpoint_advertises_produces_enumerable_in_metadata()
    {
        var metadata = EndpointMetadataFor("GET", "/minimal/compiled/issues/open");

        // TOut is IEnumerable<Issue> for OpenIssues : ICompiledListQuery<Issue>
        metadata.OfType<IProducesResponseTypeMetadata>()
            .ShouldContain(m => m.StatusCode == 200 && m.Type == typeof(IEnumerable<Issue>));
    }

    private EndpointMetadataCollection EndpointMetadataFor(string method, string pattern)
    {
        var endpoint = theHost.Services.GetServices<EndpointDataSource>()
            .SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>()
            .FirstOrDefault(x =>
                x.RoutePattern.RawText == pattern &&
                x.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method));

        endpoint.ShouldNotBeNull($"No endpoint found for {method} {pattern}");
        return endpoint.Metadata;
    }
}
