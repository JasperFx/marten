using System;
using Baseline.Dates;
using Marten.Events.Daemon.HighWater;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Daemon
{
    public class HighWaterStatisticsTests
    {
        [Fact]
        public void has_changed()
        {
            var statistics = new HighWaterStatistics {LastMark = 5L, CurrentMark = 5L};

            statistics.HasChanged.ShouldBeFalse();

            statistics.CurrentMark = 10L;
            statistics.HasChanged.ShouldBeTrue();
        }

        [Fact]
        public void caught_up()
        {
            var previous = new HighWaterStatistics {CurrentMark = 11L, HighestSequence = 20L};
            var statistics = new HighWaterStatistics();
            statistics.CurrentMark = statistics.HighestSequence = previous.HighestSequence;

            statistics.InterpretStatus(previous)
                .ShouldBe(HighWaterStatus.CaughtUp);
        }

        [Fact]
        public void changed()
        {
            var previous = new HighWaterStatistics {CurrentMark = 11L, HighestSequence = 15L};
            var statistics = new HighWaterStatistics{CurrentMark = 22L, HighestSequence = 30L};

            statistics.InterpretStatus(previous)
                .ShouldBe(HighWaterStatus.Changed);
        }

        [Fact]
        public void falling_behind_when_sequence_is_not_progressing()
        {
            var previous = new HighWaterStatistics {CurrentMark = 11L, HighestSequence = 15L, Timestamp = DateTimeOffset.UtcNow.Subtract(1.Seconds())};
            var statistics = new HighWaterStatistics{CurrentMark = 11L, HighestSequence = 15L, Timestamp = DateTimeOffset.UtcNow};

            statistics.InterpretStatus(previous)
                .ShouldBe(HighWaterStatus.Stale);
        }

        [Fact]
        public void falling_behind_when_sequence_is_progressing()
        {
            var previous = new HighWaterStatistics {CurrentMark = 11L, HighestSequence = 15L, Timestamp = DateTimeOffset.UtcNow.Subtract(1.Seconds())};
            var statistics = new HighWaterStatistics{CurrentMark = 11L, HighestSequence = 30L, Timestamp = DateTimeOffset.UtcNow};

            statistics.InterpretStatus(previous)
                .ShouldBe(HighWaterStatus.Stale);
        }

        [Fact]
        public void stale_when_sequence_is_progressing()
        {
            var previous = new HighWaterStatistics {CurrentMark = 11L, HighestSequence = 15L, Timestamp = DateTimeOffset.UtcNow.Subtract(4.Seconds())};
            var statistics = new HighWaterStatistics{CurrentMark = 11L, HighestSequence = 30L, Timestamp = DateTimeOffset.UtcNow};

            statistics.InterpretStatus(previous)
                .ShouldBe(HighWaterStatus.Stale);
        }
    }
}
