using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class get_raw_json_Tests : IntegrationContext
    {
        #region sample_get-raw-json
        [Fact]
        public async Task when_get_json_then_raw_json_should_be_returned()
        {
            var issue = new Issue { Title = "Issue 1" };

            theSession.Store(issue);
            await theSession.SaveChangesAsync();
            var json = await theSession.Query<Issue>().Where(x => x.Title == "Issue 1").ToJsonArray();
            json.ShouldNotBeNull();

            json = await theSession.Query<Issue>().ToJsonFirst();
            json = await theSession.Query<Issue>().ToJsonFirstOrDefault();
            json = await theSession.Query<Issue>().ToJsonSingle();
            json = await theSession.Query<Issue>().ToJsonSingleOrDefault();
        }
        #endregion sample_get-raw-json

        #region sample_get-raw-json-async
        [Fact]
        public async Task when_get_json_then_raw_json_should_be_returned_async()
        {
            var issue = new Issue { Title = "Issue 1" };

            theSession.Store(issue);
            await theSession.SaveChangesAsync();
            var json = await theSession.Query<Issue>().Where(x => x.Title == "Issue 1").ToJsonArray();
            json.ShouldNotBeNull();

            json = await theSession.Query<Issue>().ToJsonFirst();
            json = await theSession.Query<Issue>().ToJsonFirstOrDefault();
            json = await theSession.Query<Issue>().ToJsonSingle();
            json = await theSession.Query<Issue>().ToJsonSingleOrDefault();
        }
        #endregion sample_get-raw-json-async
        public get_raw_json_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
