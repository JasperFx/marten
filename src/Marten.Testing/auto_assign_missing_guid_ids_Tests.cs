using System;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class auto_assign_missing_guid_ids_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void should_auto_assign()
        {
            var user = new User();
            user.Id = Guid.Empty;

            theSession.Store(user);

            user.Id.ShouldNotBe(Guid.Empty);
        }

    }
}