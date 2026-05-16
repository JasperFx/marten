using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Marten.SourceGenerator.Tests;

/// <summary>
/// Test harness that runs the <see cref="CompiledQuerySourceGenerator"/> over a
/// user-supplied C# snippet. The reference set is the test project's own loaded
/// assemblies — which include the real Marten runtime via the project's
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

        // Force-load assemblies the test sources depend on. AppDomain.GetAssemblies()
        // only returns what's been touched so far — without these probes,
        // Marten/JasperFx/Npgsql wouldn't be in the reference set on first use and the
        // user source would fail to bind (CS0234 / CS0246 errors). Use Assembly.GetAssembly
        // (which actually consults the type metadata at runtime, can't be optimized out)
        // and stash the results in a field-like collection so the values are observable.
        var warmup = new[]
        {
            typeof(Marten.Linq.IMartenQueryable<>).Assembly,
            typeof(Marten.Linq.QueryStatistics).Assembly,
            typeof(JasperFx.JasperFxAssemblyAttribute).Assembly,
            typeof(Npgsql.NpgsqlParameter).Assembly,
        };

        // Build the reference set from AppDomain assemblies (BCL + xunit + Shouldly +
        // whatever the test method has touched) plus the warm-up set (Marten + JasperFx +
        // Npgsql), then dedup by location. Belt-and-suspenders against the
        // "AppDomain didn't load it yet" miss that AppDomain.GetAssemblies() alone exhibits.
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Concat(warmup)
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .GroupBy(a => a.Location)
            .Select(g => MetadataReference.CreateFromFile(g.Key))
            .Cast<MetadataReference>()
            .ToList();

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
}
