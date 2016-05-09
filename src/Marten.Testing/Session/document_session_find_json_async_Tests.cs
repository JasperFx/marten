using System;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Session
{
    public class document_session_find_json_async_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        // SAMPLE: find-json-by-id-async
        [Fact]
        public async Task when_find_then_a_json_should_be_returned()
        {
            var issue = new Issue { Title = "Issue 2" };

            theSession.Store(issue);
            await theSession.SaveChangesAsync();

            var json = await theSession.FindJsonByIdAsync<Issue>(issue.Id);
            json.ShouldBe($"{{\"Id\": \"{issue.Id}\", \"Tags\": null, \"Title\": \"Issue 2\", \"Number\": 0, \"AssigneeId\": null, \"ReporterId\": null}}");
        }
        // ENDSAMPLE

        [Fact]
        public async Task when_find_then_a_null_should_be_returned()
        {
            var json = await theSession.FindJsonByIdAsync<Issue>(Guid.NewGuid());
            json.ShouldBeNull();
        }
    }
}