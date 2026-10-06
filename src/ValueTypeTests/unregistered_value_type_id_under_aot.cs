using System;
using Marten.Schema.Identity;
using Shouldly;

namespace ValueTypeTests;

/// <summary>
/// #5600. A strong-typed document id works under Native AOT only once it is registered with
/// <c>StoreOptions.RegisterValueTypeId&lt;TDoc, TWrapper, TInner&gt;()</c> (#5589) — the reflective
/// fallback is <c>Activator.CreateInstance</c> on a generic closed over a <em>value</em> type at
/// runtime, and an AOT image has no code for that instantiation.
///
/// <para>
/// What used to reach the user in that case was
/// <c>MissingMethodException: No parameterless constructor defined for type
/// 'ValueTypeIdSelectClause`2[YourId,System.Guid]'</c>, raised while the first document's mapping was
/// built — true, and no help at all in working out that the fix is one registration call. These tests
/// pin the message that replaced it.
/// </para>
///
/// <para>
/// They can run under a JIT because <c>BuildSelectClause</c> takes <c>canEmit</c> as a parameter
/// rather than reading <c>RuntimeFeature.IsDynamicCodeSupported</c> directly — the technique #5328 used
/// for the same reason. A guard reachable only by publishing natively is a guard nothing checks.
/// </para>
/// </summary>
public class unregistered_value_type_id_under_aot
{
    private static ValueTypeIdGeneration generationFor(Type idType)
    {
        ValueTypeIdGeneration.IsCandidate(idType, out var generation).ShouldBeTrue();
        return generation!;
    }

    [Fact]
    public void names_the_registration_call_when_dynamic_code_is_unavailable()
    {
        var generation = generationFor(typeof(GuidId));

        var ex = Should.Throw<InvalidOperationException>(() =>
            ValueTypeIdGeneration.BuildSelectClause(generation, canEmit: false));

        // The three things a reader needs: which id, that AOT is why, and the exact call to add.
        ex.Message.ShouldContain("GuidId");
        ex.Message.ShouldContain("Native AOT");
        ex.Message.ShouldContain("RegisterValueTypeId<TDocument, GuidId, Guid>()");
    }

    [Fact]
    public void the_inner_type_in_the_suggested_call_follows_the_id()
    {
        // A long-backed id has to suggest <…, LongId, long>, not the Guid shape. Getting this wrong
        // would hand the reader a line that does not compile.
        var generation = generationFor(typeof(LongId));

        var ex = Should.Throw<InvalidOperationException>(() =>
            ValueTypeIdGeneration.BuildSelectClause(generation, canEmit: false));

        ex.Message.ShouldContain("RegisterValueTypeId<TDocument, LongId, long>()");
    }

    [Fact]
    public void a_jit_still_builds_the_select_clause_reflectively()
    {
        // The control. Nothing changes under a JIT: the reflective fallback is still taken, and still
        // works, for an id nobody registered.
        var generation = generationFor(typeof(GuidId));

        ValueTypeIdGeneration.BuildSelectClause(generation, canEmit: true).ShouldNotBeNull();
    }
}
