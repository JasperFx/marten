using System;
using NodaTime;
using NodaTime.Text;
using Shouldly;

namespace Marten.NodaTimePlugin.Testing;

internal static class NodaTimeShouldyExtensions
{
    private const long TicksPerMicrosecond = 10;
    private const long MaximumMillisecondDifference = 1;

    public static void ShouldBeEqualWithDbPrecision(this Instant actual, Instant expected)
    {

        var actualMicroseconds = ToUnixTimeMicroseconds(actual);
        var expectedMicroseconds = ToUnixTimeMicroseconds(expected);


        var diff = Math.Abs(actualMicroseconds - expectedMicroseconds);
        diff.AssertAwesomely(
            d => d <= MaximumMillisecondDifference,
            InstantPattern.ExtendedIso.Format(actual),
            InstantPattern.ExtendedIso.Format(expected));
    }

    private static long ToUnixTimeMicroseconds(Instant date)
    {
        return date.ToUnixTimeTicks() / TicksPerMicrosecond;
    }
}
