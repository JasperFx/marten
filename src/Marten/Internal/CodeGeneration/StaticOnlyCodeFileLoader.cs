#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;

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
            // Disambiguated against JasperFx 1.28's new
            // JasperFx.CodeGeneration.CodeFileExtensions.InitializeSynchronously,
            // which requires IAssemblyGenerator in DI. We may pass null services
            // here so route through the obsolete RuntimeCompiler overload that
            // falls back to a fresh AssemblyGenerator.
            JasperFx.RuntimeCompiler.CodeFileExtensions.InitializeSynchronously(file, rules, parent, services);
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
}
