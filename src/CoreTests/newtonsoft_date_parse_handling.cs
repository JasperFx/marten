using System;
using System.IO;
using System.Text;
using Marten.Services;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
/// #5530. <c>JsonNetSerializer</c> builds its own <c>JsonTextReader</c>, whose default
/// <c>DateParseHandling.DateTime</c> converted every date token to a <c>DateTime</c> before the
/// serializer knew the target member's type. A <c>DateTimeOffset</c> was then rebuilt from that
/// <c>DateTime</c> and silently picked up the HOST's offset.
///
/// These construct <c>JsonNetSerializer</c> directly rather than using the ambient store serializer,
/// so they assert Newtonsoft's behaviour whatever <c>DEFAULT_SERIALIZER</c> is set to.
/// </summary>
public class newtonsoft_date_parse_handling
{
    private static T RoundTrip<T>(T value)
    {
        var serializer = new JsonNetSerializer();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(serializer.ToJson(value)));
        return serializer.FromJson<T>(stream);
    }

    public class Holder
    {
        public DateTimeOffset Offset { get; set; }
        public DateTime Utc { get; set; }
        public DateTime Local { get; set; }
        public DateTime Unspecified { get; set; }
        public object? Boxed { get; set; }
    }

    /// <summary>
    /// The offset under test is deliberately <b>+05:00</b>, not UTC. A test using a UTC offset would
    /// pass against the unfixed serializer on a UTC host — which is every CI runner — and so would
    /// prove nothing there. No host is at +05:00 in CI, so this fails before the fix everywhere.
    /// </summary>
    [Fact]
    public void a_DateTimeOffset_keeps_its_own_offset_not_the_hosts()
    {
        var original = new DateTimeOffset(2026, 3, 15, 8, 30, 0, TimeSpan.FromHours(5));

        var roundTripped = RoundTrip(new Holder { Offset = original }).Offset;

        roundTripped.Offset.ShouldBe(TimeSpan.FromHours(5));
        roundTripped.ShouldBe(original);
    }

    /// <summary>
    /// The guard on the fix's collateral: leaving date tokens as strings must not disturb a DECLARED
    /// <c>DateTime</c>, whose kind and ticks the serializer still resolves from the member type.
    /// </summary>
    [Fact]
    public void declared_DateTime_members_keep_their_kind_and_ticks()
    {
        var original = new Holder
        {
            Utc = new DateTime(2026, 3, 15, 8, 30, 0, DateTimeKind.Utc),
            Local = new DateTime(2026, 3, 15, 8, 30, 0, DateTimeKind.Local),
            Unspecified = new DateTime(2026, 3, 15, 8, 30, 0, DateTimeKind.Unspecified)
        };

        var back = RoundTrip(original);

        back.Utc.Kind.ShouldBe(DateTimeKind.Utc);
        back.Utc.Ticks.ShouldBe(original.Utc.Ticks);

        back.Local.Kind.ShouldBe(DateTimeKind.Local);
        back.Local.Ticks.ShouldBe(original.Local.Ticks);

        back.Unspecified.Kind.ShouldBe(DateTimeKind.Unspecified);
        back.Unspecified.Ticks.ShouldBe(original.Unspecified.Ticks);
    }

    /// <summary>
    /// The cost of the fix, and the reason it is not finished: an <c>object</c>-typed member holding an
    /// ISO-8601 string used to come back as a <see cref="DateTime" /> and now comes back as a
    /// <see cref="string" />. That is NOT a convergence on the default serializer — <c>Marten</c>
    /// registers <c>SystemObjectNewtonsoftCompatibleConverter</c> specifically so
    /// <c>SystemTextJsonSerializer</c> returns a <c>DateTime</c> here, to match Newtonsoft. So the two
    /// now disagree where they were deliberately made to agree.
    ///
    /// Skipped rather than deleted: it documents exactly what a Newtonsoft counterpart to that
    /// converter has to restore, and it should be the first test to unskip when one exists.
    /// </summary>
    [Fact(Skip = "#5530: DateParseHandling.None breaks object-typed date compatibility; needs a Newtonsoft counterpart to SystemObjectNewtonsoftCompatibleConverter.")]
    public void an_undeclared_date_should_still_be_a_DateTime_like_the_default_serializer()
    {
        var original = new Holder { Boxed = new DateTime(2026, 3, 15, 8, 30, 0, DateTimeKind.Utc) };

        // Measured today: STJ gives DateTime, Newtonsoft with DateParseHandling.None gives string.
        var stj = new SystemTextJsonSerializer();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(stj.ToJson(original)));
        stj.FromJson<Holder>(stream).Boxed.ShouldBeOfType<DateTime>();

        RoundTrip(original).Boxed.ShouldBeOfType<DateTime>();
    }
}
