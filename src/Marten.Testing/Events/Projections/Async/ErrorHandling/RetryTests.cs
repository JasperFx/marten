using System;
using System.Threading.Tasks;
using Marten.Events.Projections.Async.ErrorHandling;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections.Async.ErrorHandling
{
    public class RetryTests
    {
        [Fact]
        public async Task should_retry_if_attempts_are_not_exceeded()
        {
            var retry = new Retry(3);

            (await retry.Handle(new Exception(), 1, new FakeMonitoredActivity()).ConfigureAwait(false))
                .ShouldBe(ExceptionAction.Retry);

            (await retry.Handle(new Exception(), 2, new FakeMonitoredActivity()).ConfigureAwait(false))
                .ShouldBe(ExceptionAction.Retry);
        }

        [Fact]
        public async Task delegates_to_after_max_attempts_when_attempts_are_exceeded()
        {
            var retry = new Retry(3) {AfterMaxAttempts = new FakeExceptionAction {Result = ExceptionAction.Stop} };

            (await retry.Handle(new Exception(), 3, new FakeMonitoredActivity()).ConfigureAwait(false))
                .ShouldBe(ExceptionAction.Stop);
        }
    }

    public class FakeExceptionAction : IExceptionAction
    {
        public bool WasCalled = false;

        public ExceptionAction Result = ExceptionAction.Nothing;

        public Task<ExceptionAction> Handle(Exception ex, int attempts, IMonitoredActivity activity)
        {
            WasCalled = true;
            Attempts = attempts;
            Activity = activity;

            return Task.FromResult(Result);
        }

        public IMonitoredActivity Activity { get; set; }

        public int Attempts { get; set; }
    }
}