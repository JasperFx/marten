using System;
using System.Reflection;
using Marten.Util;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
/// #5619. <c>RequireMethod</c> is for the reflective lookups Marten <em>invokes</em>, where a missing
/// member means Marten is broken. It replaced <c>GetMethod(...)!</c>, whose <c>!</c> is erased at runtime,
/// so a null flowed onwards and surfaced later as a bare <c>NullReferenceException</c> — or as
/// <c>MakeGenericMethod</c> throwing on a null receiver, naming neither the type nor the member.
/// </summary>
public class require_method_names_what_it_could_not_find
{
    private static void Target() { }
    private static void Target(int _) { }

    [Fact]
    public void finds_a_method_that_exists()
    {
        typeof(require_method_names_what_it_could_not_find)
            .RequireMethod(nameof(Target), BindingFlags.NonPublic | BindingFlags.Static,
                [typeof(int)])
            .GetParameters().Length.ShouldBe(1);
    }

    [Fact]
    public void names_the_type_and_the_member_when_it_does_not()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            typeof(require_method_names_what_it_could_not_find)
                .RequireMethod("NoSuchMethod", BindingFlags.NonPublic | BindingFlags.Static));

        ex.Message.ShouldContain("NoSuchMethod");
        ex.Message.ShouldContain("require_method_names_what_it_could_not_find");
        // The two causes worth naming, because they need different fixes.
        ex.Message.ShouldContain("renamed");
        ex.Message.ShouldContain("trimmer");
    }

    [Fact]
    public void renders_the_parameter_types_so_an_overload_miss_is_legible()
    {
        // A name that exists but with no matching overload is the harder failure to read, so the message
        // has to show the signature it was looking for rather than just the name.
        var ex = Should.Throw<InvalidOperationException>(() =>
            typeof(require_method_names_what_it_could_not_find)
                .RequireMethod(nameof(Target), BindingFlags.NonPublic | BindingFlags.Static,
                    [typeof(string), typeof(Guid)]));

        ex.Message.ShouldContain("Target(string, Guid)");
    }
}
