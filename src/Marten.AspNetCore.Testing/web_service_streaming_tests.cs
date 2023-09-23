using System;
using System.Linq;
using System.Threading.Tasks;
using Alba;
using IssueService.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Marten.AspNetCore.Testing;

[Collection("integration")]
public class web_service_streaming_tests: IntegrationContext
{
    private readonly IAlbaHost theHost;

    public web_service_streaming_tests(AppFixture fixture) : base(fixture)
    {
        theHost = fixture.Host;
    }

    [Theory]
    [InlineData(null)]
    [InlineData(200)]
    [InlineData(201)]
    public async Task stream_a_single_document_hit(int? onFoundStatus)
    {
        var issue = new Issue {Description = "It's bad", Open = true};

        var store = theHost.Services.GetRequiredService<IDocumentStore>();
        await using (var session = store.LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            var sendExpression = s.Get.Url($"/issue/{issue.Id}");
            if (onFoundStatus.HasValue)
            {
                sendExpression.QueryString("sc", onFoundStatus.ToString());
            }

            s.StatusCodeShouldBe(onFoundStatus ?? 200);
            s.ContentTypeShouldBe("application/json");
        });

        var read = result.ReadAsJson<Issue>();

        read.Description.ShouldBe(issue.Description);
    }

    [Fact]
    public async Task stream_a_single_document_miss()
    {
        await theHost.Scenario(s =>
        {
            s.Get.Url($"/issue/{Guid.NewGuid()}");
            s.StatusCodeShouldBe(404);
        });
    }


    [Theory]
    [InlineData(null)]
    [InlineData(200)]
    [InlineData(201)]
    public async Task stream_a_single_document_hit_2(int? onFoundStatus)
    {
        var issue = new Issue {Description = "It's bad", Open = true};

        var store = theHost.Services.GetRequiredService<IDocumentStore>();
        await using (var session = store.LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            var sendExpression = s.Get.Url($"/issue2/{issue.Id}");
            if (onFoundStatus.HasValue)
            {
                sendExpression.QueryString("sc", onFoundStatus.ToString());
            }

            s.StatusCodeShouldBe(onFoundStatus ?? 200);
            s.ContentTypeShouldBe("application/json");
        });

        var read = result.ReadAsJson<Issue>();

        read.Description.ShouldBe(issue.Description);
    }

    [Fact]
    public async Task stream_a_single_document_miss_2()
    {
        await theHost.Scenario(s =>
        {
            s.Get.Url($"/issue2/{Guid.NewGuid()}");
            s.StatusCodeShouldBe(404);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData(200)]
    [InlineData(201)]            
    public async Task stream_an_array_of_documents(int? onFoundStatus)
    {
        var store = theHost.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Issue));

        var issues = new Issue[100];
        for (int i = 0; i < issues.Length; i++)
        {
            issues[i] = Issue.Random();
        }

        await store.BulkInsertDocumentsAsync(issues);

        var result = await theHost.Scenario(s =>
        {
            var sendExpression = s.Get.Url("/issue/open");
            if (onFoundStatus.HasValue)
            {
                sendExpression.QueryString("sc", onFoundStatus.ToString());
            }

            s.StatusCodeShouldBe(onFoundStatus ?? 200);
            s.ContentTypeShouldBe("application/json");

        });

        var read = result.ReadAsJson<Issue[]>();
        read.Length.ShouldBe(issues.Count(x => x.Open));
    }


    [Theory]
    [InlineData(null)]
    [InlineData(200)]
    [InlineData(201)]
    public async Task stream_a_single_document_hit_with_compiled_query(int? onFoundStatus)
    {
        var issue = new Issue {Description = "It's bad", Open = true};

        var store = theHost.Services.GetRequiredService<IDocumentStore>();
        await using (var session = store.LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            var sendExpression = s.Get.Url($"/issue3/{issue.Id}");
            if (onFoundStatus.HasValue)
            {
                sendExpression.QueryString("sc", onFoundStatus.ToString());
            }

            s.StatusCodeShouldBe(onFoundStatus ?? 200);
            s.ContentTypeShouldBe("application/json");
        });

        var read = result.ReadAsJson<Issue>();

        read.Description.ShouldBe(issue.Description);
    }

    [Fact]
    public async Task stream_a_single_document_miss_with_compiled_query()
    {
        await theHost.Scenario(s =>
        {
            s.Get.Url($"/issue3/{Guid.NewGuid()}");
            s.StatusCodeShouldBe(404);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData(200)]
    [InlineData(201)]
    public async Task stream_an_array_of_documents_with_compiled_query(int? onFoundStatus)
    {
        var store = theHost.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Issue));

        var issues = new Issue[100];
        for (int i = 0; i < issues.Length; i++)
        {
            issues[i] = Issue.Random();
        }

        await store.BulkInsertDocumentsAsync(issues);

        var result = await theHost.Scenario(s =>
        {
            var sendExpression = s.Get.Url("/issue2/open");
            if (onFoundStatus.HasValue)
            {
                sendExpression.QueryString("sc", onFoundStatus.ToString());
            }

            s.StatusCodeShouldBe(onFoundStatus ?? 200);
            s.ContentTypeShouldBe("application/json");

        });

        var read = result.ReadAsJson<Issue[]>();
        read.Length.ShouldBe(issues.Count(x => x.Open));
    }


}
