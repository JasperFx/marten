using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Session.RequestCounter
{
    public class request_counter_with_threshold_set_Tests
    {
        [Fact]
        public void exceeding_threshold_should_invoke_provided_action()
        {
            bool wasInvoked = false;
            var requestCounter = new Marten.Services.RequestCounter(new FakeCommandRunner(), new RequestCounterThreshold(2, () => wasInvoked = true));

            requestCounter.Execute("sql");
            requestCounter.Execute("sql");
            requestCounter.Execute("sql");

            wasInvoked.ShouldBe(true); 
        }

        [Fact]
        public void not_exceeding_threshold_should_not_invoke_provided_action()
        {
            bool wasInvoked = false;
            var requestCounter = new Marten.Services.RequestCounter(new FakeCommandRunner(), new RequestCounterThreshold(2, () => wasInvoked = true));

            requestCounter.Execute("sql");
            requestCounter.Execute("sql");

            wasInvoked.ShouldBe(false);
        }
    }
}