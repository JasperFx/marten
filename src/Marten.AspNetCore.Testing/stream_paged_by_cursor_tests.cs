using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Alba;
using IssueService.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Marten.AspNetCore.Testing;

/// <summary>
/// Alba-based tests for <see cref="StreamPagedByCursor{T}"/> exercising keyset
/// (seek) pagination through a plain Minimal API endpoint.
/// </summary>
[Collection("integration")]
public class stream_paged_by_cursor_tests: IntegrationContext
{
    private readonly IAlbaHost theHost;

    public stream_paged_by_cursor_tests(AppFixture fixture) : base(fixture)
    {
        theHost = fixture.Host;
    }

    private async Task SeedIssues(params string[] descriptions)
    {
        await using var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession();
        foreach (var description in descriptions)
        {
            session.Store(new Issue { Description = description, Open = true });
        }

        await session.SaveChangesAsync();
    }

    private static CursorEnvelope ReadEnvelope(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var items = root.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("Description").GetString()!)
            .ToList();

        var nextCursor = root.GetProperty("nextCursor").ValueKind == JsonValueKind.Null
            ? null
            : root.GetProperty("nextCursor").GetString();

        return new CursorEnvelope(items, nextCursor);
    }

    private sealed record CursorEnvelope(List<string> Items, string? NextCursor);

    [Fact]
    public async Task first_page_returns_items_and_next_cursor_when_more_remain()
    {
        await SeedIssues("a", "b", "c", "d", "e");

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url("/minimal/issues/paged-cursor?pageSize=2");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var envelope = ReadEnvelope(result.ReadAsText());

        envelope.Items.ShouldBe(new[] { "a", "b" });
        envelope.NextCursor.ShouldNotBeNullOrEmpty();
        result.Context.Response.Headers["Marten-Continuation"].ToString().ShouldBe(envelope.NextCursor);
    }

    [Fact]
    public async Task subsequent_page_with_cursor_returns_next_items()
    {
        await SeedIssues("a", "b", "c", "d", "e");

        var first = await theHost.Scenario(s =>
        {
            s.Get.Url("/minimal/issues/paged-cursor?pageSize=2");
            s.StatusCodeShouldBe(200);
        });

        var firstEnvelope = ReadEnvelope(first.ReadAsText());

        var second = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issues/paged-cursor?pageSize=2&cursor={Uri.EscapeDataString(firstEnvelope.NextCursor!)}");
            s.StatusCodeShouldBe(200);
        });

        var secondEnvelope = ReadEnvelope(second.ReadAsText());

        secondEnvelope.Items.ShouldBe(new[] { "c", "d" });
        secondEnvelope.NextCursor.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task last_page_returns_remaining_items_with_no_next_cursor()
    {
        await SeedIssues("a", "b", "c", "d", "e");

        var first = await theHost.Scenario(s => s.Get.Url("/minimal/issues/paged-cursor?pageSize=2"));
        var firstEnvelope = ReadEnvelope(first.ReadAsText());

        var second = await theHost.Scenario(s =>
            s.Get.Url($"/minimal/issues/paged-cursor?pageSize=2&cursor={Uri.EscapeDataString(firstEnvelope.NextCursor!)}"));
        var secondEnvelope = ReadEnvelope(second.ReadAsText());

        var third = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/issues/paged-cursor?pageSize=2&cursor={Uri.EscapeDataString(secondEnvelope.NextCursor!)}");
            s.StatusCodeShouldBe(200);
        });

        var thirdEnvelope = ReadEnvelope(third.ReadAsText());

        thirdEnvelope.Items.ShouldBe(new[] { "e" });
        thirdEnvelope.NextCursor.ShouldBeNull();
        third.Context.Response.Headers.ContainsKey("Marten-Continuation").ShouldBeFalse();
    }

    [Fact]
    public async Task exact_multiple_of_page_size_yields_no_next_cursor_on_final_page()
    {
        await SeedIssues("a", "b", "c", "d");

        var first = await theHost.Scenario(s => s.Get.Url("/minimal/issues/paged-cursor?pageSize=2"));
        var firstEnvelope = ReadEnvelope(first.ReadAsText());
        firstEnvelope.NextCursor.ShouldNotBeNullOrEmpty();

        var second = await theHost.Scenario(s =>
            s.Get.Url($"/minimal/issues/paged-cursor?pageSize=2&cursor={Uri.EscapeDataString(firstEnvelope.NextCursor!)}"));
        var secondEnvelope = ReadEnvelope(second.ReadAsText());

        secondEnvelope.Items.ShouldBe(new[] { "c", "d" });
        secondEnvelope.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task empty_result_set_returns_empty_items_and_no_cursor()
    {
        var result = await theHost.Scenario(s =>
        {
            s.Get.Url("/minimal/issues/paged-cursor?pageSize=5");
            s.StatusCodeShouldBe(200);
        });

        var envelope = ReadEnvelope(result.ReadAsText());

        envelope.Items.ShouldBeEmpty();
        envelope.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task mixed_sort_directions_paginate_correctly()
    {
        // OrderByDescending(Description).ThenBy(Id) — descending primary key, ascending tie-breaker
        await SeedIssues("a", "b", "c", "d", "e");

        var first = await theHost.Scenario(s => s.Get.Url("/minimal/issues/paged-cursor-mixed?pageSize=2"));
        var firstEnvelope = ReadEnvelope(first.ReadAsText());
        firstEnvelope.Items.ShouldBe(new[] { "e", "d" });
        firstEnvelope.NextCursor.ShouldNotBeNullOrEmpty();

        var second = await theHost.Scenario(s =>
            s.Get.Url($"/minimal/issues/paged-cursor-mixed?pageSize=2&cursor={Uri.EscapeDataString(firstEnvelope.NextCursor!)}"));
        var secondEnvelope = ReadEnvelope(second.ReadAsText());
        secondEnvelope.Items.ShouldBe(new[] { "c", "b" });
        secondEnvelope.NextCursor.ShouldNotBeNullOrEmpty();

        var third = await theHost.Scenario(s =>
            s.Get.Url($"/minimal/issues/paged-cursor-mixed?pageSize=2&cursor={Uri.EscapeDataString(secondEnvelope.NextCursor!)}"));
        var thirdEnvelope = ReadEnvelope(third.ReadAsText());
        thirdEnvelope.Items.ShouldBe(new[] { "a" });
        thirdEnvelope.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task duplicate_leading_sort_key_values_are_disambiguated_by_terminal_tie_breaker()
    {
        // Several issues share the same Description; the ThenBy(Id) tie-breaker
        // must still produce a stable, non-overlapping, exhaustive pagination.
        await SeedIssues("same", "same", "same", "same", "same");

        var seen = new List<string>();
        string? cursor = null;
        for (var i = 0; i < 10; i++)
        {
            var result = await theHost.Scenario(s =>
            {
                s.Get.Url(cursor == null
                    ? "/minimal/issues/paged-cursor?pageSize=2"
                    : $"/minimal/issues/paged-cursor?pageSize=2&cursor={Uri.EscapeDataString(cursor)}");
            });

            var envelope = ReadEnvelope(result.ReadAsText());
            seen.AddRange(envelope.Items);
            cursor = envelope.NextCursor;

            if (cursor == null)
            {
                break;
            }
        }

        seen.Count.ShouldBe(5);
        seen.ShouldAllBe(x => x == "same");
    }
}
