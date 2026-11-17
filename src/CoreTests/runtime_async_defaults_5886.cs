using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using JasperFx.CodeGeneration;
using Marten;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
///     #5886, Critter Stack plan decision D4: runtime-async is on by default for the net11.0 build of Marten's own
///     assemblies (MartenRuntimeAsync in Directory.Build.props). net10.0 cannot load runtime-async code, so it stays
///     off there. Modelled on Wolverine's CoreTests.Compilation.runtime_async_defaults_5150.
/// </summary>
public class runtime_async_defaults_5886
{
    // MethodImplAttributes.Async, the flag the compiler sets on a runtime-async method
    private const MethodImplAttributes AsyncFlag = (MethodImplAttributes)0x2000;

    [Fact]
    public void the_marten_assembly_is_compiled_with_runtime_async_only_on_net11()
    {
        var types = typeof(DocumentStore).Assembly.GetTypes();

        var runtimeAsyncMethods = types
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                          BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Count(m => (m.MethodImplementationFlags & AsyncFlag) != 0);

        var stateMachines = types.Count(t =>
            typeof(IAsyncStateMachine).IsAssignableFrom(t) && t.IsDefined(typeof(CompilerGeneratedAttribute)));

#if NET11_0_OR_GREATER
        runtimeAsyncMethods.ShouldBeGreaterThan(0);

        // Async iterators are not converted by runtime-async, so their state machines remain
        stateMachines.ShouldBeLessThan(runtimeAsyncMethods / 10);
#else
        runtimeAsyncMethods.ShouldBe(0);
        stateMachines.ShouldBeGreaterThan(100);
#endif
    }

    [Fact]
    public void runtime_compiled_code_uses_runtime_async_by_default_only_on_net11()
    {
        // Marten itself no longer compiles code at runtime, but anything that does through JasperFx's
        // GenerationRules (Wolverine handlers, for one) gets the same default
#if NET11_0_OR_GREATER
        new GenerationRules().UseRuntimeAsync.ShouldBeTrue();
#else
        new GenerationRules().UseRuntimeAsync.ShouldBeFalse();
#endif
    }
}
