using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;

namespace Marten.SourceGenerator.Tests;

/// <summary>
/// PoC validation suite for the typed-parameter-binder emission added in iteration 2
/// of #4405. These tests exercise the generator end-to-end using a mock Marten
/// contract surface — see <see cref="GeneratorDriverHarness"/>.
/// </summary>
public class CompiledQuerySourceGeneratorTests
{
    [Fact]
    public void emits_nothing_when_assembly_lacks_jasperfx_marker()
    {
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            public class TargetDoc { }
            public class FindByName : ICompiledQuery<TargetDoc, TargetDoc>
            {
                public string Name = string.Empty;
                public Expression<Func<IMartenQueryable<TargetDoc>, TargetDoc>> QueryIs() => null!;
            }
            """;
        var result = GeneratorDriverHarness.Run(src, addJasperFxAttribute: false);
        result.GeneratedTrees.ShouldBeEmpty();
    }

    [Fact]
    public void emits_handler_class_per_discovered_compiled_query()
    {
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            namespace Sample
            {
                public class TargetDoc { }
                public class FindByName : ICompiledQuery<TargetDoc, TargetDoc>
                {
                    public string Name = string.Empty;
                    public Expression<Func<IMartenQueryable<TargetDoc>, TargetDoc>> QueryIs() => null!;
                }
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        var generated = result.GeneratedTrees.Single().ToString();
        generated.ShouldContain("internal static class FindByName_CompiledQueryHandler");
        generated.ShouldContain("public const string DiscoveredQueryFullName = \"Sample.FindByName\";");
        generated.ShouldContain("public const string DocTypeFullName = \"Sample.TargetDoc\";");
    }

    [Fact]
    public void bind_parameter_switch_uses_direct_property_read_for_string()
    {
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            public class TargetDoc { }
            public class FindByName : ICompiledQuery<TargetDoc, TargetDoc>
            {
                public string Name = string.Empty;
                public Expression<Func<IMartenQueryable<TargetDoc>, TargetDoc>> QueryIs() => null!;
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        var generated = result.GeneratedTrees.Single().ToString();
        generated.ShouldContain("case \"Name\":");
        generated.ShouldContain("parameter.NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Varchar;");
        generated.ShouldContain("parameter.Value = (object?)query.Name ?? global::System.DBNull.Value;");
    }

    [Fact]
    public void bind_parameter_handles_int_long_guid_bool_datetime()
    {
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            public class TargetDoc { }
            public class MixedTypes : ICompiledQuery<TargetDoc, TargetDoc>
            {
                public int IntVal;
                public long LongVal;
                public Guid GuidVal;
                public bool BoolVal;
                public DateTime DateVal;
                public DateTimeOffset DateOffsetVal;
                public Expression<Func<IMartenQueryable<TargetDoc>, TargetDoc>> QueryIs() => null!;
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        var generated = result.GeneratedTrees.Single().ToString();
        generated.ShouldContain("NpgsqlDbType.Integer");
        generated.ShouldContain("NpgsqlDbType.Bigint");
        generated.ShouldContain("NpgsqlDbType.Uuid");
        generated.ShouldContain("NpgsqlDbType.Boolean");
        generated.ShouldContain("NpgsqlDbType.Timestamp");
        generated.ShouldContain("NpgsqlDbType.TimestampTz");
    }

    [Fact]
    public void classifies_querystatistics_as_statistics_member()
    {
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            public class TargetDoc { }
            public class WithStats : ICompiledQuery<TargetDoc, TargetDoc>
            {
                public string Name = string.Empty;
                public QueryStatistics Stats = new();
                public Expression<Func<IMartenQueryable<TargetDoc>, TargetDoc>> QueryIs() => null!;
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        var generated = result.GeneratedTrees.Single().ToString();
        generated.ShouldContain("StatisticsMemberName = \"Stats\";");
        generated.ShouldContain("ParameterMemberNames = new string[] { \"Name\" };");
        generated.ShouldNotContain("case \"Stats\":");
    }

    [Fact]
    public void classifies_action_ilist_idictionary_as_include_members()
    {
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;
            using System.Collections.Generic;

            public class TargetDoc { }
            public class IncludeOther { }
            public class WithIncludes : ICompiledQuery<TargetDoc, TargetDoc>
            {
                public string Name = string.Empty;
                public Action<IncludeOther> Reader = _ => { };
                public IList<IncludeOther> Listed = new List<IncludeOther>();
                public IDictionary<Guid, IncludeOther> Mapped = new Dictionary<Guid, IncludeOther>();
                public Expression<Func<IMartenQueryable<TargetDoc>, TargetDoc>> QueryIs() => null!;
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        var generated = result.GeneratedTrees.Single().ToString();
        generated.ShouldContain("IncludeMemberNames = new string[] { \"Reader\", \"Listed\", \"Mapped\" };");
        generated.ShouldContain("ParameterMemberNames = new string[] { \"Name\" };");
    }

    [Fact]
    public void arrays_of_supported_element_types_are_parameters_not_includes()
    {
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            public class TargetDoc { }
            public class WithArrays : ICompiledQuery<TargetDoc, TargetDoc>
            {
                public string[] Names = Array.Empty<string>();
                public int[] Ints = Array.Empty<int>();
                public Guid[] Ids = Array.Empty<Guid>();
                public Expression<Func<IMartenQueryable<TargetDoc>, TargetDoc>> QueryIs() => null!;
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        var generated = result.GeneratedTrees.Single().ToString();
        generated.ShouldContain("ParameterMemberNames = new string[] { \"Names\", \"Ints\", \"Ids\" };");
        generated.ShouldContain("NpgsqlDbType.Array | global::NpgsqlTypes.NpgsqlDbType.Varchar");
        generated.ShouldContain("NpgsqlDbType.Array | global::NpgsqlTypes.NpgsqlDbType.Integer");
        generated.ShouldContain("NpgsqlDbType.Array | global::NpgsqlTypes.NpgsqlDbType.Uuid");
    }

    [Fact]
    public void byte_array_emits_bytea_not_array_composite()
    {
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            public class TargetDoc { }
            public class WithBlob : ICompiledQuery<TargetDoc, TargetDoc>
            {
                public byte[] Blob = Array.Empty<byte>();
                public Expression<Func<IMartenQueryable<TargetDoc>, TargetDoc>> QueryIs() => null!;
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        var generated = result.GeneratedTrees.Single().ToString();
        generated.ShouldContain("NpgsqlDbType.Bytea");
        generated.ShouldNotContain("NpgsqlDbType.Array | global::NpgsqlTypes.NpgsqlDbType");
    }

    [Fact]
    public void enum_parameter_emits_dual_branch_dispatch_on_enumAsString()
    {
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            public class TargetDoc { }
            public enum Severity { Low, Medium, High }
            public class WithEnum : ICompiledQuery<TargetDoc, TargetDoc>
            {
                public Severity Level;
                public Expression<Func<IMartenQueryable<TargetDoc>, TargetDoc>> QueryIs() => null!;
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        var generated = result.GeneratedTrees.Single().ToString();
        generated.ShouldContain("if (enumAsString)");
        generated.ShouldContain("parameter.NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Varchar;");
        generated.ShouldContain("parameter.Value = query.Level.ToString();");
        generated.ShouldContain("parameter.NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Integer;");
        generated.ShouldContain("parameter.Value = (int)query.Level;");
    }

    [Fact]
    public void nullable_value_type_member_is_skipped_with_diagnostic()
    {
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            public class TargetDoc { }
            public class WithNullable : ICompiledQuery<TargetDoc, TargetDoc>
            {
                public string Name = string.Empty;
                public int? Optional;
                public Expression<Func<IMartenQueryable<TargetDoc>, TargetDoc>> QueryIs() => null!;
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        var generated = result.GeneratedTrees.Single().ToString();
        generated.ShouldNotContain("case \"Optional\":");
        generated.ShouldContain("ParameterMemberNames = new string[] { \"Name\" };");

        var diagnostic = result.Results.Single().Diagnostics.Single();
        diagnostic.Id.ShouldBe("MTSG001");
        diagnostic.GetMessage().ShouldContain("Optional");
    }

    [Fact]
    public void marten_ignore_attribute_skips_member()
    {
        var src = """
            using Marten;
            using Marten.Linq;
            using Marten.Events.CodeGeneration;
            using System.Linq.Expressions;
            using System;

            public class TargetDoc { }
            public class WithIgnored : ICompiledQuery<TargetDoc, TargetDoc>
            {
                public string Name = string.Empty;
                [MartenIgnore] public string Ignored = string.Empty;
                public Expression<Func<IMartenQueryable<TargetDoc>, TargetDoc>> QueryIs() => null!;
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        var generated = result.GeneratedTrees.Single().ToString();
        generated.ShouldNotContain("case \"Ignored\":");
        generated.ShouldContain("ParameterMemberNames = new string[] { \"Name\" };");
    }

    [Fact]
    public void multiple_compiled_queries_emit_independent_handlers()
    {
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            public class DocA { }
            public class DocB { }
            public class FindA : ICompiledQuery<DocA, DocA>
            {
                public string Name = "";
                public Expression<Func<IMartenQueryable<DocA>, DocA>> QueryIs() => null!;
            }
            public class FindB : ICompiledQuery<DocB, DocB>
            {
                public Guid Id;
                public Expression<Func<IMartenQueryable<DocB>, DocB>> QueryIs() => null!;
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        result.GeneratedTrees.Length.ShouldBe(2);
        var combined = string.Concat(result.GeneratedTrees.Select(t => t.ToString()));
        combined.ShouldContain("FindA_CompiledQueryHandler");
        combined.ShouldContain("FindB_CompiledQueryHandler");
    }

    [Fact]
    public void registration_shim_has_module_initializer_attribute()
    {
        // Iteration 3: the generator emits a [ModuleInitializer]-decorated Register
        // method alongside the handler class. At assembly load the runtime registry
        // gets populated implicitly — no consumer call required, no StoreOptions flag.
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            public class TargetDoc { }
            public class FindByName : ICompiledQuery<TargetDoc, TargetDoc>
            {
                public string Name = string.Empty;
                public Expression<Func<IMartenQueryable<TargetDoc>, TargetDoc>> QueryIs() => null!;
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        var generated = result.GeneratedTrees.Single().ToString();
        generated.ShouldContain("internal static class FindByName_CompiledQueryHandler_Registration");
        generated.ShouldContain("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        generated.ShouldContain("CompiledQueryHandlerRegistry.Register(");
        generated.ShouldContain("typeof(global::FindByName)");
    }

    [Fact]
    public void registration_shim_uses_static_lambda_for_zero_allocation_box()
    {
        // The boxing adapter must be a `static` lambda — it must not capture state
        // (e.g. `this` or local variables), so the C# compiler caches it as a single
        // static readonly delegate field. Per-call cost is one virtual delegate
        // invoke + one unbox cast; no Func<> allocation per call.
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            public class TargetDoc { }
            public class Boxy : ICompiledQuery<TargetDoc, TargetDoc>
            {
                public string Name = string.Empty;
                public Expression<Func<IMartenQueryable<TargetDoc>, TargetDoc>> QueryIs() => null!;
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        var generated = result.GeneratedTrees.Single().ToString();
        generated.ShouldContain("bindParameter: static (parameter, query, memberName, enumAsString)");
        generated.ShouldContain("=> Boxy_CompiledQueryHandler.BindParameter(");
        generated.ShouldContain("(global::Boxy)query");
    }

    [Fact]
    public void registration_shim_passes_descriptor_metadata_through()
    {
        // The descriptor surface must propagate query/doc/output Types + the three
        // member-name lists exactly as the iteration-2 handler exposes them. This
        // is the contract the runtime registry consumer (CompiledQueryCollection)
        // reads in iteration 3d.
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;
            using System.Collections.Generic;

            namespace Ns
            {
                public class Doc { }
                public class IncludeT { }
                public class Mixed : ICompiledQuery<Doc, Doc>
                {
                    public string Name = string.Empty;
                    public QueryStatistics Stats = new();
                    public IList<IncludeT> Children = new List<IncludeT>();
                    public Expression<Func<IMartenQueryable<Doc>, Doc>> QueryIs() => null!;
                }
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        var generated = result.GeneratedTrees.Single().ToString();
        generated.ShouldContain("queryType: typeof(global::Ns.Mixed)");
        generated.ShouldContain("docType: typeof(global::Ns.Doc)");
        generated.ShouldContain("outputType: typeof(global::Ns.Doc)");
        generated.ShouldContain("parameterMemberNames: Mixed_CompiledQueryHandler.ParameterMemberNames");
        generated.ShouldContain("includeMemberNames: Mixed_CompiledQueryHandler.IncludeMemberNames");
        generated.ShouldContain("statisticsMemberName: Mixed_CompiledQueryHandler.StatisticsMemberName");
    }

    [Fact]
    public void generated_source_compiles_against_real_marten_and_npgsql()
    {
        // Compile-check the generated source by running the driver and feeding the
        // output back into a fresh compilation that includes minimal Npgsql stubs.
        // This catches syntactic / namespace / type-resolution regressions.
        var src = """
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;
            using System.Collections.Generic;

            namespace Foo
            {
                public class Doc { }
                public class IncludeT { }
                public class Sample : ICompiledQuery<Doc, Doc>
                {
                    public string Name = "";
                    public int Age;
                    public Guid Id;
                    public string[] Tags = Array.Empty<string>();
                    public byte[] Blob = Array.Empty<byte>();
                    public QueryStatistics Stats = new();
                    public Action<IncludeT> Reader = _ => { };
                    public Expression<Func<IMartenQueryable<Doc>, Doc>> QueryIs() => null!;
                }
            }
            """;
        var result = GeneratorDriverHarness.Run(src);
        var generated = result.GeneratedTrees.Single().ToString();

        // Compile the user source + generated source against the test project's loaded
        // assemblies — these include the real Marten + Npgsql via ProjectReference, so
        // the [ModuleInitializer] shim's references to CompiledQueryHandlerRegistry +
        // CompiledQueryHandlerDescriptor resolve. Any binding errors from the generator
        // output show up as compilation errors here.
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "VerifyAssembly",
            new[]
            {
                Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("[assembly: JasperFx.JasperFxAssembly]"),
                Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(src),
                Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(generated)
            },
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(a.Location)),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: Microsoft.CodeAnalysis.NullableContextOptions.Enable));

        var diagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        diagnostics.ShouldBeEmpty(string.Join("\n", diagnostics.Select(d => d.ToString())));
    }
}
