using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Marten.SourceGenerator.Tests;

/// <summary>
/// Test harness that runs the <see cref="CompiledQuerySourceGenerator"/> over a
/// user-supplied C# snippet. The reference set is the test project's own loaded assembly closure —
/// which includes the real Marten runtime and everything it reaches, via the project's
/// <c>&lt;ProjectReference&gt;</c>. That keeps the tests honest: the generator's
/// metadata-name checks resolve against the same canonical types
/// (<c>Marten.Linq.ICompiledQuery&lt;,&gt;</c>, <c>Marten.Linq.QueryStatistics</c>)
/// that the runtime planner sees.
/// </summary>
internal static class GeneratorDriverHarness
{
    public static GeneratorDriverRunResult Run(string userSource, bool addJasperFxAttribute = true)
    {
        var sources = addJasperFxAttribute
            ? new[] { "[assembly: JasperFx.JasperFxAssembly]", userSource }
            : new[] { userSource };

        var syntaxTrees = sources
            .Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest)))
            .ToArray();

        // Force-load the assemblies the snippets bind against, then reference everything loaded.
        //
        // AppDomain.GetAssemblies() alone returns only what this process has already touched, so
        // the reference set becomes a function of test ORDER rather than of the code under test.
        // A hand-written warm-up list of four types used to paper over that, and it held right up
        // until the project reached CI (#5096): generated_source_compiles_against_real_marten_and_
        // npgsql passed locally and failed on a fresh runner with CS0012 on Weasel.Storage, an
        // assembly reachable through Marten's surface, absent from the list, and already loaded
        // locally by an earlier test in the same process.
        //
        // So walk the reference closure from those roots instead of naming assemblies one at a
        // time — the next transitive dependency to appear in generated code then needs no edit
        // here. Loading goes through the runtime's binder rather than by file path ON PURPOSE:
        // the output directory also holds netstandard facades (an otherwise empty
        // System.Linq.Expressions.dll of type-forwards, pulled in by the netstandard2.0 generator
        // project's dependencies), and referencing one of those in place of the runtime's real
        // assembly resolves Expression<> and IQueryable<> to stubs and fails every test with
        // CS1069. The binder picks by identity and never has that problem.
        forceLoadClosure(
        [
            typeof(Marten.Linq.IMartenQueryable<>).Assembly,
            typeof(Marten.Linq.QueryStatistics).Assembly,
            typeof(JasperFx.JasperFxAssemblyAttribute).Assembly,
            typeof(Npgsql.NpgsqlParameter).Assembly,
            typeof(GeneratorDriverHarness).Assembly
        ]);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .GroupBy(a => a.Location)
            .Select(g => tryReference(g.Key))
            .Where(reference => reference is not null)
            .ToList()!;

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        // Fail fast if the user-supplied source doesn't compile. Without this guard,
        // a typo in a test source silently yields zero generator output (no syntax
        // tree → no ICompiledQuery binding → no transform hit), and the test fails
        // with a confusing "Sequence contains no elements" downstream.
        var preDiagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (preDiagnostics.Length > 0)
        {
            throw new InvalidOperationException(
                "Test source has compilation errors before the generator runs:\n  "
                + string.Join("\n  ", preDiagnostics.Select(d => d.ToString())));
        }

        var generator = new CompiledQuerySourceGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        return driver.RunGenerators(compilation).GetRunResult();
    }

    /// <summary>
    /// Loads every assembly reachable from <paramref name="roots"/>, so the reference set built
    /// from <c>AppDomain.GetAssemblies()</c> afterwards is the closure the test project actually
    /// compiles against rather than whatever this process happened to touch first.
    /// </summary>
    private static void forceLoadClosure(Assembly[] roots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            walk(root);
        }

        void walk(Assembly assembly)
        {
            if (!seen.Add(assembly.FullName!)) return;

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                try
                {
                    walk(Assembly.Load(reference));
                }
                catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    // A reference that cannot be resolved at runtime is one the snippets cannot
                    // bind against either, so it has nothing to contribute to the reference set.
                }
            }
        }
    }

    /// <summary>
    /// A metadata reference for one file, or null when it is not a managed assembly.
    /// </summary>
    private static MetadataReference? tryReference(string path)
    {
        try
        {
            return MetadataReference.CreateFromFile(path);
        }
        catch (Exception e) when (e is BadImageFormatException or IOException)
        {
            return null;
        }
    }
}
