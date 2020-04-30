using System;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class auto_assign_missing_guid_ids_Tests : IntegrationContext
    {
        [Fact]
        public void should_auto_assign()
        {
            var user = new User();
            user.Id = Guid.Empty;

            theSession.Store(user);

            user.Id.ShouldNotBe(Guid.Empty);
        }

        public auto_assign_missing_guid_ids_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
