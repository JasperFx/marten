using System;
using Baseline.Dates;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Resiliency;
using Marten.Exceptions;
using Marten.Testing.Events.Aggregation;
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
                .ShouldBeOfType<StopShard>();

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
                .ShouldBeOfType<PauseShard>();
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
                .ShouldBeOfType<StopShard>();

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
                .ShouldBe(new PauseShard(5.Seconds()));

        }

        [Fact]
        public void determine_continuation_for_skip_falls_back_to_stop_if_not_apply_event_exception()
        {
            var settings = new DaemonSettings();
            settings.OnException<BadImageFormatException>().SkipEvent();

            settings.DetermineContinuation(new BadImageFormatException(), 0)
                .ShouldBeOfType<StopShard>();
        }

        [Fact]
        public void determine_continuation_on_skip_with_apply_event_exception()
        {
            var settings = new DaemonSettings();
            settings.OnApplyEventException().SkipEvent();

            var @event = new Event<AEvent>(new AEvent()) {Sequence = 55};

            settings.DetermineContinuation(new ApplyEventException(@event, new ArithmeticException()), 0)
                .ShouldBeOfType<SkipEvent>()
                .Event.Sequence.ShouldBe(@event.Sequence);
        }
    }
}
