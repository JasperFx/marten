using System;
using Marten.Services;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_654_document_session_delete_Tests : DocumentSessionFixture<IdentityMap>
    {
        [Fact]
        public void upsert_then_delete_should_delete()
        {
            var issue = new Issue { Id = Guid.NewGuid() };

            theSession.Store(issue);
            theSession.Delete<Issue>(issue.Id);
            theSession.SaveChanges();

            var loadedIssue = theSession.Load<Issue>(issue.Id);
            loadedIssue.ShouldBeNull();
        }
    }
}