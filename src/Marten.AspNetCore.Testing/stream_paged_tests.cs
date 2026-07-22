using System;
using System.Linq;
using System.Threading.Tasks;
using Alba;
using IssueService.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Marten.AspNetCore.Testing;

/// <summary>
/// Alba-based tests for <see cref="StreamPaged{T}"/> executing against a plain
/// Minimal API endpoint (no Wolverine required).
/// </summary>
[Collection("integration")]
public class stream_paged_tests: IntegrationContext
{
    private readonly IAlbaHost theHost;

    public stream_paged_tests(AppFixture fixture) : base(fixture)
    {
        theHost = fixture.Host;
    }

    private class PagedEnvelope<T>
    {
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalItemCount { get; set; }
        public int PageCount { get; set; }
        public bool HasNextPage { get; set; }
        public bool HasPreviousPage { get; set; }
        public T[] Items { get; set; }
    }

    private async Task seedOpenIssues(int count, string prefix)
    {
        await using var session = theHost.Services.GetRequiredService<IDocumentStore>().LightweightSession();
        for (var i = 0; i < count; i++)
        {
            session.Store(new Issue { Description = $"{prefix}_{i:D3}", Open = true });
        }

        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task first_page_of_multiple_pages()
    {
        await seedOpenIssues(10, "page1_" + Guid.NewGuid().ToString("N")[..8]);

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url("/minimal/issues/paged/1/3");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var envelope = result.ReadAsJson<PagedEnvelope<Issue>>();

        envelope.PageNumber.ShouldBe(1);
        envelope.PageSize.ShouldBe(3);
        envelope.TotalItemCount.ShouldBe(10);
        envelope.PageCount.ShouldBe(4);
        envelope.HasNextPage.ShouldBeTrue();
        envelope.HasPreviousPage.ShouldBeFalse();
        envelope.Items.Length.ShouldBe(3);
    }

    [Fact]
    public async Task middle_page_of_multiple_pages()
    {
        await seedOpenIssues(10, "page2_" + Guid.NewGuid().ToString("N")[..8]);

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url("/minimal/issues/paged/2/3");
            s.StatusCodeShouldBe(200);
        });

        var envelope = result.ReadAsJson<PagedEnvelope<Issue>>();

        envelope.PageNumber.ShouldBe(2);
        envelope.PageSize.ShouldBe(3);
        envelope.TotalItemCount.ShouldBe(10);
        envelope.PageCount.ShouldBe(4);
        envelope.HasNextPage.ShouldBeTrue();
        envelope.HasPreviousPage.ShouldBeTrue();
        envelope.Items.Length.ShouldBe(3);
    }

    [Fact]
    public async Task last_page_may_be_partial()
    {
        await seedOpenIssues(10, "page3_" + Guid.NewGuid().ToString("N")[..8]);

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url("/minimal/issues/paged/4/3");
            s.StatusCodeShouldBe(200);
        });

        var envelope = result.ReadAsJson<PagedEnvelope<Issue>>();

        envelope.PageNumber.ShouldBe(4);
        envelope.PageSize.ShouldBe(3);
        envelope.TotalItemCount.ShouldBe(10);
        envelope.PageCount.ShouldBe(4);
        envelope.HasNextPage.ShouldBeFalse();
        envelope.HasPreviousPage.ShouldBeTrue();
        envelope.Items.Length.ShouldBe(1);
    }

    [Fact]
    public async Task single_page_result()
    {
        await seedOpenIssues(3, "page4_" + Guid.NewGuid().ToString("N")[..8]);

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url("/minimal/issues/paged/1/25");
            s.StatusCodeShouldBe(200);
        });

        var envelope = result.ReadAsJson<PagedEnvelope<Issue>>();

        envelope.PageNumber.ShouldBe(1);
        envelope.PageSize.ShouldBe(25);
        envelope.TotalItemCount.ShouldBe(3);
        envelope.PageCount.ShouldBe(1);
        envelope.HasNextPage.ShouldBeFalse();
        envelope.HasPreviousPage.ShouldBeFalse();
        envelope.Items.Length.ShouldBe(3);
    }

    [Fact]
    public async Task empty_result_set()
    {
        var result = await theHost.Scenario(s =>
        {
            s.Get.Url("/minimal/issues/paged/1/10");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var envelope = result.ReadAsJson<PagedEnvelope<Issue>>();

        envelope.PageNumber.ShouldBe(1);
        envelope.PageSize.ShouldBe(10);
        envelope.TotalItemCount.ShouldBe(0);
        envelope.PageCount.ShouldBe(0);
        envelope.HasNextPage.ShouldBeFalse();
        envelope.HasPreviousPage.ShouldBeFalse();
        envelope.Items.ShouldNotBeNull();
        envelope.Items.Length.ShouldBe(0);
    }

    [Fact]
    public async Task empty_result_set_on_page_beyond_first_reports_has_previous_page()
    {
        var result = await theHost.Scenario(s =>
        {
            s.Get.Url("/minimal/issues/paged/2/10");
            s.StatusCodeShouldBe(200);
        });

        var envelope = result.ReadAsJson<PagedEnvelope<Issue>>();

        envelope.TotalItemCount.ShouldBe(0);
        envelope.HasPreviousPage.ShouldBeTrue();
        envelope.HasNextPage.ShouldBeFalse();
        envelope.Items.Length.ShouldBe(0);
    }

    [Fact]
    public async Task items_are_in_expected_order_across_pages()
    {
        var prefix = "order_" + Guid.NewGuid().ToString("N")[..8];
        await seedOpenIssues(10, prefix);

        var page1 = await theHost.Scenario(s => s.Get.Url("/minimal/issues/paged/1/5"));
        var page2 = await theHost.Scenario(s => s.Get.Url("/minimal/issues/paged/2/5"));

        var envelope1 = page1.ReadAsJson<PagedEnvelope<Issue>>();
        var envelope2 = page2.ReadAsJson<PagedEnvelope<Issue>>();

        var allDescriptions = envelope1.Items.Select(x => x.Description)
            .Concat(envelope2.Items.Select(x => x.Description))
            .ToArray();

        allDescriptions.ShouldBe(allDescriptions.OrderBy(x => x).ToArray());
        allDescriptions.Distinct().Count().ShouldBe(10);
    }
}
