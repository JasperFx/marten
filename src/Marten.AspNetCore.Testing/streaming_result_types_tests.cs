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
        // VersionlessDoc is registered with Metadata.Version.Enabled = false, so there is no
        // mt_version column to derive an ETag from. EmitETag defaults to true, but the inline
        // version read comes back null and no ETag (and no false 304) is produced.
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
