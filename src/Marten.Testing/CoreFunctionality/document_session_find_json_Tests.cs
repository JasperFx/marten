using System;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class document_session_find_json_Tests: IntegrationContext
    {
        #region sample_find-json-by-id
        [Fact]
        public void when_find_then_a_json_should_be_returned()
        {
            var issue = new Issue { Title = "Issue 1" };

            theSession.Store(issue);
            theSession.SaveChanges();

            var json = theSession.Json.FindById<Issue>(issue.Id);
            json.ShouldBe($"{{\"Id\": \"{issue.Id}\", \"Tags\": null, \"BugId\": null, \"Title\": \"Issue 1\", \"Number\": 0, \"AssigneeId\": null, \"ReporterId\": null}}");
        }

        #endregion sample_find-json-by-id

        [Fact]
        public void when_find_then_a_null_should_be_returned()
        {
            var json = theSession.Json.FindById<Issue>(Guid.NewGuid());
            json.ShouldBeNull();
        }

        public document_session_find_json_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
