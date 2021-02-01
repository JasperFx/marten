using System;
using Baseline.Dates;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Resiliency;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing.Resiliency
{
    public class DaemonSettings_continuation_handling
    {
        [Fact]
        public void unknown_exception_type_just_gets_a_stop()
        {
            var settings = new DaemonSettings();
            settings.OnException<BadImageFormatException>()
                .RetryLater(1.Seconds(), 2.Seconds(), 3.Seconds());

            settings.DetermineContinuation(new DivideByZeroException(), 0)
                .ShouldBeOfType<StopProjection>();

        }

        [Fact]
        public void default_rules_for_npgsql_exception()
        {
            var settings = new DaemonSettings();
            settings.DetermineContinuation(new NpgsqlException(), 0)
                .ShouldBeOfType<RetryLater>();

            settings.DetermineContinuation(new NpgsqlException(), 1)
                .ShouldBeOfType<RetryLater>();

            settings.DetermineContinuation(new NpgsqlException(), 2)
                .ShouldBeOfType<RetryLater>();

            settings.DetermineContinuation(new NpgsqlException(), 3)
                .ShouldBeOfType<PauseProjection>();
        }

        [Fact]
        public void retry_logic_with_no_additional_continuation()
        {
            var settings = new DaemonSettings();
            settings.OnException<BadImageFormatException>()
                .RetryLater(1.Seconds(), 2.Seconds(), 3.Seconds());

            settings.DetermineContinuation(new BadImageFormatException(), 0)
                .ShouldBe(new RetryLater(1.Seconds()));

            settings.DetermineContinuation(new BadImageFormatException(), 1)
                .ShouldBe(new RetryLater(2.Seconds()));

            settings.DetermineContinuation(new BadImageFormatException(), 2)
                .ShouldBe(new RetryLater(3.Seconds()));

            settings.DetermineContinuation(new BadImageFormatException(), 3)
                .ShouldBeOfType<StopProjection>();

        }

        [Fact]
        public void retry_logic_then_pause()
        {
            var settings = new DaemonSettings();
            settings.OnException<BadImageFormatException>()
                .RetryLater(1.Seconds(), 2.Seconds(), 3.Seconds())
                .Then.Pause(5.Seconds());

            settings.DetermineContinuation(new BadImageFormatException(), 0)
                .ShouldBe(new RetryLater(1.Seconds()));

            settings.DetermineContinuation(new BadImageFormatException(), 1)
                .ShouldBe(new RetryLater(2.Seconds()));

            settings.DetermineContinuation(new BadImageFormatException(), 2)
                .ShouldBe(new RetryLater(3.Seconds()));

            settings.DetermineContinuation(new BadImageFormatException(), 3)
                .ShouldBe(new PauseProjection(5.Seconds()));

        }
    }
}
