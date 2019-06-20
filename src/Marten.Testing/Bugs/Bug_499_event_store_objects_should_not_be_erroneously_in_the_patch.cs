using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_499_event_store_objects_should_not_be_erroneously_in_the_patch: IntegratedFixture
    {
        [Fact]
        public void not_using_the_event_store_should_not_be_in_patch()
        {
            StoreOptions(_ => _.Schema.For<User>());

            var patch = theStore.Schema.ToPatch(false);

            patch.UpdateDDL.ShouldNotContain("mt_events");
            patch.UpdateDDL.ShouldNotContain("mt_streams");
        }
    }
}
