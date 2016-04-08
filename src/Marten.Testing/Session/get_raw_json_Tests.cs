using System.Linq;
using Marten.Util;
using Xunit;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using System.Threading.Tasks;

namespace Marten.Testing.Session
{
    class get_raw_json_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        // SAMPLE: get-raw-json
        [Fact]
        public void when_get_json_then_raw_json_should_be_returned()
        {
            var issue = new Issue { Title = "Issue 1" };

            theSession.Store(issue);
            theSession.SaveChanges();

            var json = theSession.Query<Issue>().Where(x => x.Title == "Issue 1").ToListJson();
            json.ShouldBe($"[{{\"Id\": \"{issue.Id}\", \"Tags\": null, \"Title\": \"Issue 1\", \"AssigneeId\": null, \"ReporterId\": null}}]");
            json = theSession.Query<Issue>().FirstJson();
            json.ShouldBe($"{{\"Id\": \"{issue.Id}\", \"Tags\": null, \"Title\": \"Issue 1\", \"AssigneeId\": null, \"ReporterId\": null}}");
            json = theSession.Query<Issue>().FirstOrDefaultJson();
            json = theSession.Query<Issue>().SingleJson();
            json = theSession.Query<Issue>().SingleOrDefaultJson();
        }
        // ENDSAMPLE

        // SAMPLE: get-raw-json-async
        [Fact]
        public async Task when_get_json_then_raw_json_should_be_returned_async()
        {
            var issue = new Issue { Title = "Issue 1" };

            theSession.Store(issue);
            theSession.SaveChanges();

            var json = await theSession.Query<Issue>().Where(x => x.Title == "Issue 1").ToListJsonAsync();
            json.ShouldBe($"[{{\"Id\": \"{issue.Id}\", \"Tags\": null, \"Title\": \"Issue 1\", \"AssigneeId\": null, \"ReporterId\": null}}]");
            json = await theSession.Query<Issue>().FirstJsonAsync();
            json.ShouldBe($"{{\"Id\": \"{issue.Id}\", \"Tags\": null, \"Title\": \"Issue 1\", \"AssigneeId\": null, \"ReporterId\": null}}");
            json = await theSession.Query<Issue>().FirstOrDefaultJsonAsync();
            json = await theSession.Query<Issue>().SingleJsonAsync();
            json = await theSession.Query<Issue>().SingleOrDefaultJsonAsync();
        }
        // ENDSAMPLE
    }
}
