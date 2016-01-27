using Marten.Services;
using Xunit;

namespace Marten.Testing.Session.RequestCounter
{
    public class request_counter_threshold_Tests
    {
        [Fact]
        public void no_threshold_value_set_should_return_false_for_HasThreshold()
        {
            var requestCounterThreshold = new RequestCounterThreshold(0, () => { });
            requestCounterThreshold.HasThreshold.ShouldBeFalse();
        }

        [Fact]
        public void threshold_value_set_should_return_true_for_HasThreshold()
        {
            var requestCounterThreshold = new RequestCounterThreshold(1, () => { });
            requestCounterThreshold.HasThreshold.ShouldBeTrue();
        }

        [Fact]
        public void ValidateCounter_should_not_invoke_action_if_threshold_is_not_exceeded()
        {
            bool wasInvoked = false;
            var requestCounterThreshold = new RequestCounterThreshold(5, () => { wasInvoked = true; });
            requestCounterThreshold.ValidateCounter(1);

            wasInvoked.ShouldBeFalse();
        }

        [Fact]
        public void ValidateCounter_should_invoke_action_if_threshold_is_exceeded()
        {
            bool wasInvoked = false;
            var requestCounterThreshold = new RequestCounterThreshold(5, () => { wasInvoked = true; });
            requestCounterThreshold.ValidateCounter(6);

            wasInvoked.ShouldBeTrue();
        }
    }
}