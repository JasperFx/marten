using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Marten.Internal.CompiledQueries;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Npgsql;
using NpgsqlTypes;
using Shouldly;
using Xunit;

namespace Marten.SourceGenerator.Tests;

/// <summary>
/// End-to-end validation of #4405 iteration 3: the generator-emitted
/// <c>[ModuleInitializer]</c> shim fires at assembly load and populates the real
/// <see cref="CompiledQueryHandlerRegistry"/>, and the registered descriptor's
/// <see cref="CompiledQueryHandlerDescriptor.BindParameter"/> delegate binds the
/// expected value/type onto a real <see cref="NpgsqlParameter"/>.
/// </summary>
/// <remarks>
/// <para>
/// The textual generator tests in <c>CompiledQuerySourceGeneratorTests</c>
/// verify the emitted source <i>looks</i> right. This suite proves it actually
/// <i>runs</i> right — compiles the generated source into a real
/// <see cref="Assembly"/> in memory, loads it into the test
/// <see cref="AppDomain"/>, triggers <see cref="RuntimeHelpers.RunModuleConstructor"/>
/// to fire the module init, then asks the live registry whether the descriptor
/// is there and behaves correctly.
/// </para>
/// <para>
/// Each test uses a uniquely-named query type so the process-wide registry
/// doesn't collide between test runs.
/// </para>
/// </remarks>
public class EndToEndRegistrationTests
{
    [Fact]
    public void module_initializer_registers_handler_with_real_registry()
    {
        var queryClassName = $"E2EQuery_{Guid.NewGuid():N}";
        var src = $$"""
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            public class {{queryClassName}}_Doc { }
            public class {{queryClassName}} : ICompiledQuery<{{queryClassName}}_Doc, {{queryClassName}}_Doc>
            {
                public string Name = string.Empty;
                public Expression<Func<IMartenQueryable<{{queryClassName}}_Doc>, {{queryClassName}}_Doc>> QueryIs() => null!;
            }
            """;

        var (assembly, queryType) = CompileAndLoad(src, queryClassName);

        // Sanity — the loaded assembly contains the generator output.
        assembly.GetTypes().Select(t => t.Name)
            .ShouldContain($"{queryClassName}_CompiledQueryHandler");
        assembly.GetTypes().Select(t => t.Name)
            .ShouldContain($"{queryClassName}_CompiledQueryHandler_Registration");

        // After firing module init, the real registry has the descriptor.
        CompiledQueryHandlerRegistry.TryGet(queryType, out var descriptor).ShouldBeTrue();
        descriptor!.QueryType.ShouldBe(queryType);
        descriptor.ParameterMemberNames.ShouldBe(new[] { "Name" });
        descriptor.IncludeMemberNames.ShouldBeEmpty();
        descriptor.StatisticsMemberName.ShouldBeNull();
    }

    [Fact]
    public void registered_descriptor_binds_parameter_value_via_direct_property_read()
    {
        var queryClassName = $"E2EBind_{Guid.NewGuid():N}";
        var src = $$"""
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            public class {{queryClassName}}_Doc { }
            public class {{queryClassName}} : ICompiledQuery<{{queryClassName}}_Doc, {{queryClassName}}_Doc>
            {
                public string Name = string.Empty;
                public int Age;
                public Guid Id;
                public Expression<Func<IMartenQueryable<{{queryClassName}}_Doc>, {{queryClassName}}_Doc>> QueryIs() => null!;
            }
            """;

        var (_, queryType) = CompileAndLoad(src, queryClassName);

        CompiledQueryHandlerRegistry.TryGet(queryType, out var descriptor).ShouldBeTrue();

        // Construct an instance of the user's query type, populate its members,
        // then drive the descriptor's BindParameter exactly as the runtime planner
        // does — once per (NpgsqlParameter, memberName) pairing.
        var query = Activator.CreateInstance(queryType)!;
        SetField(query, "Name", "Bilbo");
        SetField(query, "Age", 111);
        var id = Guid.NewGuid();
        SetField(query, "Id", id);

        var nameParam = new NpgsqlParameter();
        descriptor!.BindParameter(nameParam, query, "Name", false /* enumAsString */);
        nameParam.NpgsqlDbType.ShouldBe(NpgsqlDbType.Varchar);
        nameParam.Value.ShouldBe("Bilbo");

        var ageParam = new NpgsqlParameter();
        descriptor.BindParameter(ageParam, query, "Age", false /* enumAsString */);
        ageParam.NpgsqlDbType.ShouldBe(NpgsqlDbType.Integer);
        ageParam.Value.ShouldBe(111);

        var idParam = new NpgsqlParameter();
        descriptor.BindParameter(idParam, query, "Id", false /* enumAsString */);
        idParam.NpgsqlDbType.ShouldBe(NpgsqlDbType.Uuid);
        idParam.Value.ShouldBe(id);
    }

    [Fact]
    public void enum_member_branches_on_enumAsString_flag_at_runtime()
    {
        var queryClassName = $"E2EEnum_{Guid.NewGuid():N}";
        var src = $$"""
            using Marten;
            using Marten.Linq;
            using System.Linq.Expressions;
            using System;

            public class {{queryClassName}}_Doc { }
            public enum {{queryClassName}}_Severity { Low, Medium, High }
            public class {{queryClassName}} : ICompiledQuery<{{queryClassName}}_Doc, {{queryClassName}}_Doc>
            {
                public {{queryClassName}}_Severity Level;
                public Expression<Func<IMartenQueryable<{{queryClassName}}_Doc>, {{queryClassName}}_Doc>> QueryIs() => null!;
            }
            """;

        var (assembly, queryType) = CompileAndLoad(src, queryClassName);
        var enumType = assembly.GetType($"{queryClassName}_Severity")!;

        CompiledQueryHandlerRegistry.TryGet(queryType, out var descriptor).ShouldBeTrue();

        var query = Activator.CreateInstance(queryType)!;
        SetField(query, "Level", Enum.ToObject(enumType, 2)); // "High"

        var paramAsInt = new NpgsqlParameter();
        descriptor!.BindParameter(paramAsInt, query, "Level", false /* enumAsString */);
        paramAsInt.NpgsqlDbType.ShouldBe(NpgsqlDbType.Integer);
        paramAsInt.Value.ShouldBe(2);

        var paramAsString = new NpgsqlParameter();
        descriptor.BindParameter(paramAsString, query, "Level", true /* enumAsString */);
        paramAsString.NpgsqlDbType.ShouldBe(NpgsqlDbType.Varchar);
        paramAsString.Value.ShouldBe("High");
    }

    /// <summary>
    /// Runs the generator over <paramref name="userSource"/>, compiles the user
    /// code + generated code into an in-memory assembly that references the real
    /// Marten + Npgsql runtimes, loads it into the test AppDomain, fires module
    /// initializers, and returns the loaded <see cref="Assembly"/> + the query
    /// type's runtime <see cref="Type"/>.
    /// </summary>
    private static (Assembly Assembly, Type QueryType) CompileAndLoad(string userSource, string queryClassName)
    {
        // Step 1: run the generator over user source to produce the handler + module-init shim.
        // The harness adds [assembly: JasperFx.JasperFxAssembly] which gates the generator —
        // without it, GeneratedTrees would be empty (the implicit-opt-in behavior we ship).
        var driverResult = GeneratorDriverHarness.Run(userSource);

        // Step 2: build a fresh compilation that includes the user code + generated trees.
        // The [JasperFxAssembly] attribute on the loaded assembly is NOT required at runtime —
        // it's only the generator's compile-time gate. Once the registration shim exists,
        // it fires from its [ModuleInitializer] regardless of the assembly's own attributes.
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(userSource),
        }
        .Concat(driverResult.GeneratedTrees)
        .ToArray();

        // Reference set: BCL + Marten + Npgsql + JasperFx + Weasel. We grab everything
        // currently loaded into the test AppDomain — Marten + its transitive deps were
        // brought in by the ProjectReference, so this is sufficient.
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: $"E2E_{Guid.NewGuid():N}",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())));

        // Step 3: load the assembly and force module init to run. Assembly.Load alone
        // doesn't fire [ModuleInitializer] — those run on first access to any member
        // in the module. RunModuleConstructor is the explicit, deterministic trigger.
        peStream.Position = 0;
        var assembly = Assembly.Load(peStream.ToArray());
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);

        var queryType = assembly.GetType(queryClassName)
            ?? throw new InvalidOperationException($"Loaded assembly does not contain type {queryClassName}");
        return (assembly, queryType);
    }

    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Field {name} not found on {target.GetType().Name}");
        field.SetValue(target, value);
    }
}
