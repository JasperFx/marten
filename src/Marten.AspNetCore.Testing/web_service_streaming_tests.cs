using System;
using System.Linq;
using System.Threading.Tasks;
using Alba;
using IssueService;
using IssueService.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Marten.AspNetCore.Testing
{
    public class AppFixture : IDisposable, IAsyncLifetime
    {
        private IAlbaHost _host;

        public AppFixture()
        {
        }

        public void Dispose()
        {
            _host.Dispose();
        }

        public IAlbaHost Host => _host;

        public async Task InitializeAsync()
        {
            _host = await Program.CreateHostBuilder(Array.Empty<string>())
                .StartAlbaAsync();
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }
    }

    public class web_service_streaming_tests : IClassFixture<AppFixture>
    {
        private readonly IAlbaHost theHost;

        public web_service_streaming_tests(AppFixture fixture)
        {
            theHost = fixture.Host;
        }

        [Fact]
        public async Task stream_a_single_document_hit()
        {
            var issue = new Issue {Description = "It's bad", Open = true};

            var store = theHost.Services.GetRequiredService<IDocumentStore>();
            using (var session = store.LightweightSession())
            {
                session.Store(issue);
                await session.SaveChangesAsync();
            }

            var result = await theHost.Scenario(s =>
            {
                s.Get.Url($"/issue/{issue.Id}");
                s.StatusCodeShouldBeOk();
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


        [Fact]
        public async Task stream_a_single_document_hit_2()
        {
            var issue = new Issue {Description = "It's bad", Open = true};

            var store = theHost.Services.GetRequiredService<IDocumentStore>();
            using (var session = store.LightweightSession())
            {
                session.Store(issue);
                await session.SaveChangesAsync();
            }

            var result = await theHost.Scenario(s =>
            {
                s.Get.Url($"/issue2/{issue.Id}");
                s.StatusCodeShouldBeOk();
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

        [Fact]
        public async Task stream_an_array_of_documents()
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
                s.Get.Url("/issue/open");
                s.StatusCodeShouldBeOk();
                s.ContentTypeShouldBe("application/json");

            });

            var read = result.ReadAsJson<Issue[]>();
            read.Length.ShouldBe(issues.Count(x => x.Open));
        }


        [Fact]
        public async Task stream_a_single_document_hit_with_compiled_query()
        {
            var issue = new Issue {Description = "It's bad", Open = true};

            var store = theHost.Services.GetRequiredService<IDocumentStore>();
            using (var session = store.LightweightSession())
            {
                session.Store(issue);
                await session.SaveChangesAsync();
            }

            var result = await theHost.Scenario(s =>
            {
                s.Get.Url($"/issue3/{issue.Id}");
                s.StatusCodeShouldBeOk();
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

        [Fact]
        public async Task stream_an_array_of_documents_with_compiled_query()
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
                s.Get.Url("/issue2/open");
                s.StatusCodeShouldBeOk();
                s.ContentTypeShouldBe("application/json");

            });

            var read = result.ReadAsJson<Issue[]>();
            read.Length.ShouldBe(issues.Count(x => x.Open));
        }


    }
}
