using System;
using Marten.Testing.Documents;
using Shouldly;

namespace Marten.Testing
{
    public class auto_assign_missing_guid_ids_Tests : DocumentSessionFixture
    {
        public void should_auto_assign()
        {
            var user = new User();
            user.Id = Guid.Empty;

            theSession.Store(user);

            user.Id.ShouldNotBe(Guid.Empty);
        }

    }
}