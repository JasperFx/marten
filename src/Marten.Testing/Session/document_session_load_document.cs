using System;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Session
{
    public class document_session_load_document : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void when_id_setter_is_private()
        {
            var issue = new UserWithPrivateId();

            theSession.Store(issue);
            theSession.SaveChanges();

            issue.Id.ShouldNotBe(Guid.Empty);

            var user = theSession.Load<Issue>(issue.Id);
            user.ShouldBeNull();
        }

        [Fact]
        public void when_no_id_setter()
        {
            var issue = new UserWithoutIdSetter();

            theSession.Store(issue);
            theSession.SaveChanges();

            issue.Id.ShouldBe(Guid.Empty);
        }
    }
}