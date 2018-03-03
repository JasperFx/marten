using System.Linq;
using Marten.Util;
using Xunit;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using System.Threading.Tasks;

namespace Marten.Testing.Session
{
    public class get_raw_json_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        // SAMPLE: get-raw-json
        //[Fact]
        public void when_get_json_then_raw_json_should_be_returned()
        {
            var issue = new Issue { Title = "Issue 1" };

            theSession.Store(issue);
            theSession.SaveChanges();

            var json = theSession.Query<Issue>().Where(x => x.Title == "Issue 1").ToJsonArray();
            json.ShouldBe($"[{{\"Id\": \"{issue.Id}\", \"Tags\": null, \"Title\": \"Issue 1\", \"AssigneeId\": null, \"ReporterId\": null}}]");
            json = theSession.Query<Issue>().AsJson().First();
            json.ShouldBe($"{{\"Id\": \"{issue.Id}\", \"Tags\": null, \"Title\": \"Issue 1\", \"AssigneeId\": null, \"ReporterId\": null}}");
            json = theSession.Query<Issue>().AsJson().FirstOrDefault();
            json = theSession.Query<Issue>().AsJson().Single();
            json = theSession.Query<Issue>().AsJson().SingleOrDefault();
        }
        // ENDSAMPLE

        // SAMPLE: get-raw-json-async
        //[Fact]
        public async Task when_get_json_then_raw_json_should_be_returned_async()
        {
            var issue = new Issue { Title = "Issue 1" };

            theSession.Store(issue);
            theSession.SaveChanges();

            var json = await theSession.Query<Issue>().Where(x => x.Title == "Issue 1").ToJsonArrayAsync().ConfigureAwait(false);
            json.ShouldBe($"[{{\"Id\": \"{issue.Id}\", \"Tags\": null, \"Title\": \"Issue 1\", \"AssigneeId\": null, \"ReporterId\": null}}]");
            json = await theSession.Query<Issue>().AsJson().FirstAsync().ConfigureAwait(false);
            json.ShouldBe($"{{\"Id\": \"{issue.Id}\", \"Tags\": null, \"Title\": \"Issue 1\", \"AssigneeId\": null, \"ReporterId\": null}}");
            json = await theSession.Query<Issue>().AsJson().FirstOrDefaultAsync().ConfigureAwait(false);
            json = await theSession.Query<Issue>().AsJson().SingleAsync().ConfigureAwait(false);
            json = await theSession.Query<Issue>().AsJson().SingleOrDefaultAsync().ConfigureAwait(false);
        }
        // ENDSAMPLE
    }
}
