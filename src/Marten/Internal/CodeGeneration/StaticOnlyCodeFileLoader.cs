#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using JasperFx.RuntimeCompiler;
using Microsoft.Extensions.DependencyInjection;

namespace Marten.Internal.CodeGeneration;

/// <summary>
/// Wraps the per-call-site decision between
/// (a) "load a pre-generated type from the application assembly" and
/// (b) "fall back to JasperFx.RuntimeCompiler to compile a fresh assembly"
/// behind <see cref="StoreOptions.AllowRuntimeCodeGeneration"/>.
/// </summary>
/// <remarks>
/// <para>
///     When <paramref name="allowRuntimeCodeGeneration"/> is <c>true</c>
///     (the default), behavior is identical to the pre-9.0 path: dispatch to
///     <c>JasperFx.RuntimeCompiler.CodeFileExtensions.InitializeSynchronously</c>,
///     which Roslyn-compiles a fresh assembly when the pre-built type is
///     absent.
/// </para>
/// <para>
///     When <c>false</c> — the AOT-friendly mode (#4309) — only the
///     pre-generated path is allowed. <see cref="ICodeFile.AttachTypesSynchronously"/>
///     runs against <see cref="GenerationRules.ApplicationAssembly"/>; if the
///     type isn't there, this method throws <see cref="InvalidOperationException"/>
///     with a message pointing at the missing artifact rather than silently
///     compiling.
/// </para>
/// <para>
///     Marked <c>[RequiresDynamicCode]</c> so the IL trimmer / Native AOT
///     publisher recognizes this as a dynamic-codegen entry point and emits
///     warnings on call sites that haven't opted out via the flag.
/// </para>
/// </remarks>
internal static class StaticOnlyCodeFileLoader
{
    [RequiresDynamicCode(
        "Marten may invoke JasperFx.RuntimeCompiler when AllowRuntimeCodeGeneration is true. " +
        "Set StoreOptions.AllowRuntimeCodeGeneration = false and pre-generate all code files to make this AOT-clean.")]
    public static void Initialize(
        ICodeFile file,
        GenerationRules rules,
        ICodeFileCollection parent,
        IServiceProvider? services,
        bool allowRuntimeCodeGeneration)
    {
        if (allowRuntimeCodeGeneration)
        {
            // 2.0: JasperFx.CodeGeneration.CodeFileExtensions.InitializeSynchronously
            // dispatches through GenerationRules.Loader to the configured
            // ITypeLoader. The Dynamic / Auto loaders need an IAssemblyGenerator
            // resolvable from `services` — the legacy `?? new AssemblyGenerator()`
            // fallback was removed. When the host hasn't registered one (true
            // for the bulk of Marten's call sites that pass `services: null`),
            // wrap the original provider with a stub that resolves
            // IAssemblyGenerator to a fresh AssemblyGenerator. This preserves
            // pre-9.0 behavior under Dynamic mode without requiring every
            // consuming app to do a manual DI registration.
            var effectiveServices = services ?? RuntimeCompilerFallback.Instance;
            file.InitializeSynchronously(rules, parent, effectiveServices);
            return;
        }

        // Static-only path: refuse to compile. AttachTypesSynchronously sees
        // whether a pre-generated type for this code file is loadable from the
        // ApplicationAssembly (typically the entry assembly under Static mode
        // or the assembly the user pinned via StoreOptions.ApplicationAssembly).
        var @namespace = parent.ToNamespace(rules);
        var found = file.AttachTypesSynchronously(rules, rules.ApplicationAssembly, services, @namespace);
        if (!found)
        {
            throw new InvalidOperationException(
                $"StoreOptions.AllowRuntimeCodeGeneration is false but the pre-generated type for code file '{file.FileName}' " +
                $"was not found in {rules.ApplicationAssembly.FullName}. " +
                "Either run the Marten codegen tooling to emit the missing type, or set " +
                "StoreOptions.AllowRuntimeCodeGeneration = true to allow Roslyn-based compilation at runtime.");
        }
    }

    /// <summary>
    /// Minimal IServiceProvider that resolves only IAssemblyGenerator. Stands in
    /// for the legacy `?? new AssemblyGenerator()` fallback that was removed in
    /// JasperFx 2.0's package-level split (#190 Tier-2). Marten call sites that
    /// pass `services: null` (most of them) would otherwise hit
    /// `InvalidOperationException: register IAssemblyGenerator in DI`. We don't
    /// want every consuming app to have to wire that up by hand for a default
    /// experience that just worked before.
    /// </summary>
    private sealed class RuntimeCompilerFallback : IServiceProvider
    {
        public static readonly RuntimeCompilerFallback Instance = new();

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IAssemblyGenerator))
            {
                return new AssemblyGenerator();
            }
            return null;
        }
    }
}
