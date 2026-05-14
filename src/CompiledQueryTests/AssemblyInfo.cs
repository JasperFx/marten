using Marten.Testing.Harness;
using Xunit;

// Opt this assembly into Marten.SourceGenerator's scanning. With this attribute
// present, the generator finds every ICompiledQuery<,> implementation defined in
// this project and emits a typed handler + a [ModuleInitializer] that registers
// the handler with Marten.Internal.CompiledQueries.CompiledQueryHandlerRegistry
// at assembly load. Without it the generator emits nothing — the implicit-opt-in
// contract for #4405's final V9 behavior.

[assembly: JasperFx.JasperFxAssembly]

// Disable xunit's default test-class-level parallelism for this assembly. The
// perf gate temporarily Unregisters / Re-registers descriptors in the process-wide
// CompiledQueryHandlerRegistry to drive its codegen-vs-source-gen A/B; running it
// alongside the correctness gate's "source_gen_path_is_actually_engaged" assertion
// would race on the registry state. Matches the convention used elsewhere in the
// Marten test suite (LinqTests/AssemblyInfo.cs).
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CompiledQueryTests;

// xunit collection fixtures are per-assembly: the [CollectionDefinition("integration")]
// in Marten.Testing.Harness can't be inherited across assemblies. Redeclare it here so
// tests derived from IntegrationContext get the shared DefaultStoreFixture wired in.
[CollectionDefinition("integration")]
public class IntegrationCollection: ICollectionFixture<DefaultStoreFixture>
{
}
