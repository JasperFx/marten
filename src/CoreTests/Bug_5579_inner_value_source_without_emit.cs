#nullable enable
using System;
using Marten.Schema.Identity;
using Shouldly;
using Xunit;

namespace CoreTests;

public readonly record struct Bug5579PaymentId(Guid Value);

public readonly record struct Bug5579InvoiceId(string Value);

public readonly record struct Bug5579SequenceId(long Value);

public class Bug5579Wrapper
{
    public Bug5579Wrapper(Guid value) => Value = value;
    public Guid Value { get; }
}

/// <summary>
/// #5579. Reading the inner primitive out of a strong-typed id used to go through
/// FastExpressionCompiler unconditionally, which is Reflection.Emit underneath and therefore throws
/// <c>PlatformNotSupportedException</c> in a Native AOT image. JasperFx 2.80.0 fixed the wrapping half
/// (<c>ValueTypeInfo.CreateWrapper</c>/<c>UnWrapper</c>); these two accessors are Marten's own and were
/// left behind, so the package bump alone did not make strong-typed ids work under AOT.
/// </summary>
/// <remarks>
/// These run under a JIT, so they prove the reflective fallback produces the same answer as the emitted
/// delegate -- not that AOT works. The AOT half is a real runtime read in Marten.AotRuntimeSmoke; a clean
/// AOT <i>publish</i> proves nothing (#5328).
/// </remarks>
public class Bug_5579_inner_value_source_without_emit
{
    private static void AgreesBothWays<TOuter, TInner>(TOuter instance, TInner expected)
    {
        var property = typeof(TOuter).GetProperty(nameof(Bug5579PaymentId.Value))!;

        StrongTypedIdValueSource.Build<TInner>(typeof(TOuter), property, canEmit: true)(instance!)
            .ShouldBe(expected);

        StrongTypedIdValueSource.Build<TInner>(typeof(TOuter), property, canEmit: false)(instance!)
            .ShouldBe(expected);
    }

    [Fact]
    public void reads_a_guid_backed_readonly_record_struct()
    {
        var value = Guid.NewGuid();
        AgreesBothWays(new Bug5579PaymentId(value), value);
    }

    [Fact]
    public void reads_a_string_backed_readonly_record_struct()
        => AgreesBothWays(new Bug5579InvoiceId("INV-1138"), "INV-1138");

    [Fact]
    public void reads_a_long_backed_readonly_record_struct()
        => AgreesBothWays(new Bug5579SequenceId(9_223_372_036_854_775_806L), 9_223_372_036_854_775_806L);

    /// <summary>
    /// The reference-type wrapper shape, which only the F# discriminated-union path reaches in practice.
    /// Both id strategies now share one accessor, so it is worth pinning that the boxing-free reflective
    /// call works for a class as well as a struct.
    /// </summary>
    [Fact]
    public void reads_a_class_wrapper()
    {
        var value = Guid.NewGuid();
        AgreesBothWays(new Bug5579Wrapper(value), value);
    }

    [Fact]
    public void a_property_with_no_getter_is_reported_rather_than_throwing_a_null_reference()
    {
        var property = typeof(Bug5579SetOnly).GetProperty(nameof(Bug5579SetOnly.Value))!;

        Should.Throw<ArgumentOutOfRangeException>(() =>
            StrongTypedIdValueSource.Build<Guid>(typeof(Bug5579SetOnly), property, canEmit: false));
    }

    private class Bug5579SetOnly
    {
        private Guid _value;
        public Guid Value { set => _value = value; }
    }
}
