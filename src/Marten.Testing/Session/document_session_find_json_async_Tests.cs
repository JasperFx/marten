using System;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Session
{
    public class document_session_find_json_async_Tests: DocumentSessionFixture<NulloIdentityMap>
    {
        // SAMPLE: find-json-by-id-async
        [Fact]
        public async Task when_find_then_a_json_should_be_returned()
        {
            var issue = new Issue { Title = "Issue 2" };

            theSession.Store(issue);
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var json = await theSession.Json.FindByIdAsync<Issue>(issue.Id).ConfigureAwait(false);
            json.ShouldBe($"{{\"Id\": \"{issue.Id}\", \"Tags\": null, \"BugId\": null, \"Title\": \"Issue 2\", \"Number\": 0, \"AssigneeId\": null, \"ReporterId\": null}}");
        }

        // ENDSAMPLE

        [Fact]
        public async Task when_find_then_a_null_should_be_returned()
        {
            var json = await theSession.Json.FindByIdAsync<Issue>(Guid.NewGuid()).ConfigureAwait(false);
            json.ShouldBeNull();
        }
    }
}