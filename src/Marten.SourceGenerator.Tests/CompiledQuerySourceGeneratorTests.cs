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
    public void generated_source_compiles_against_npgsql_stub()
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

        // Build a fresh compilation that includes the generated source + minimal Npgsql stubs.
        const string npgsqlStub = """
            namespace Npgsql { public class NpgsqlParameter { public NpgsqlTypes.NpgsqlDbType NpgsqlDbType { get; set; } public object? Value { get; set; } } }
            namespace NpgsqlTypes { [System.Flags] public enum NpgsqlDbType { Varchar = 0x1, Integer = 0x2, Bigint = 0x4, Uuid = 0x8, Boolean = 0x10, Timestamp = 0x20, TimestampTz = 0x40, Numeric = 0x80, Real = 0x100, Double = 0x200, Char = 0x400, Bytea = 0x800, Smallint = 0x1000, Interval = 0x2000, Oid = 0x4000, Date = 0x8000, Time = 0x10000, Array = 0x20000 } }
            """;

        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "VerifyAssembly",
            new[]
            {
                Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(GeneratorDriverHarness.MartenContractStub),
                Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(npgsqlStub),
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
