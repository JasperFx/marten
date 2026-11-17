using System.IO;
using System.Text;
using Marten.Services;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
/// #5876. <c>JsonNetSerializer</c> reads with <c>MetadataPropertyHandling.ReadAhead</c>, which buffers each
/// object as a <c>JObject</c>; the reader's default <c>FloatParseHandling.Double</c> turned every fractional
/// token into a <c>double</c> there and a <c>decimal</c> member was rebuilt from it. .NET 11 stopped rounding
/// that conversion, so <c>123.45m</c> came back as <c>123.45000000000000284217094304m</c>.
///
/// Constructs <c>JsonNetSerializer</c> directly, so it asserts Newtonsoft's behaviour whatever
/// <c>DEFAULT_SERIALIZER</c> is.
/// </summary>
public class newtonsoft_decimal_parse_handling
{
    public class Holder
    {
        public decimal Amount { get; set; }
        public double Ratio { get; set; }
    }

    public record Money(decimal Amount);

    private static T Read<T>(string json)
    {
        return new JsonNetSerializer().FromJson<T>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void a_decimal_member_keeps_exactly_the_digits_in_the_json()
    {
        Read<Holder>("{\"Amount\":123.45,\"Ratio\":0.1}").Amount.ShouldBe(123.45m);
        Read<Holder>("{\"Amount\":0.1234567890123456789012345678}").Amount.ShouldBe(0.1234567890123456789012345678m);
    }

    [Fact]
    public void a_decimal_constructor_parameter_keeps_exactly_the_digits_in_the_json()
    {
        Read<Money>("{\"Amount\":99.95}").Amount.ShouldBe(99.95m);
    }

    [Fact]
    public void a_double_member_is_unaffected()
    {
        Read<Holder>("{\"Amount\":1,\"Ratio\":0.1}").Ratio.ShouldBe(0.1d);
    }
}
