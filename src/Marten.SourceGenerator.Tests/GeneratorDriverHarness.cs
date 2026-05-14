using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Marten.SourceGenerator;

namespace Marten.SourceGenerator.Tests;

/// <summary>
/// Test harness that runs the <see cref="CompiledQuerySourceGenerator"/> over a
/// user-supplied C# snippet plus a minimal in-memory stand-in for
/// <c>Marten.Linq.ICompiledQuery&lt;TDoc, TOut&gt;</c> and
/// <c>JasperFx.JasperFxAssemblyAttribute</c>.
/// </summary>
/// <remarks>
/// <para>
/// The PoC test project intentionally does <b>not</b> reference Marten itself —
/// the generator only depends on Roslyn symbol shape, not on Marten runtime types.
/// That keeps the generator's unit tests fast and isolated from the rest of the
/// solution's PostgreSQL test dependencies. The contract types are reproduced here
/// at their canonical names so the generator's metadata-name checks resolve.
/// </para>
/// </remarks>
internal static class GeneratorDriverHarness
{
    public const string MartenContractStub = """
        namespace System.Linq.Expressions { public class Expression<T> { } }
        namespace Marten { public interface IMartenQueryable<T> { } public class QueryStatistics { } }
        namespace Marten.Linq
        {
            public interface ICompiledQueryMarker { }
            public interface ICompiledQuery<TDoc, TOut> : ICompiledQueryMarker where TDoc : notnull
            {
                System.Linq.Expressions.Expression<System.Func<Marten.IMartenQueryable<TDoc>, TOut>> QueryIs();
            }
        }
        namespace JasperFx
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly)]
            public sealed class JasperFxAssemblyAttribute : System.Attribute { }
        }
        namespace Marten.Events.CodeGeneration
        {
            [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Property)]
            public sealed class MartenIgnoreAttribute : System.Attribute { }
        }
        """;

    public static GeneratorDriverRunResult Run(string userSource, bool addJasperFxAttribute = true)
    {
        var sources = addJasperFxAttribute
            ? new[] { MartenContractStub, "[assembly: JasperFx.JasperFxAssembly]", userSource }
            : new[] { MartenContractStub, userSource };

        var syntaxTrees = sources
            .Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest)))
            .ToArray();

        // Minimal reference set — just BCL. The contract stubs above provide all the
        // Marten/JasperFx surface the generator inspects.
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new CompiledQuerySourceGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        return driver.RunGenerators(compilation).GetRunResult();
    }
}
