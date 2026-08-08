using Marten.Testing.Harness;
using Xunit;

namespace StressTests;

/// <summary>
/// The "integration" collection definition for THIS assembly.
///
/// <para>xUnit resolves <c>[CollectionDefinition]</c> per test assembly. StressTests inherits
/// <see cref="IntegrationContext"/> — and therefore its <c>[Collection("integration")]</c> — from a
/// type that is compiled into EventSourcingTests and reaches here across a ProjectReference, so the
/// attribute arrived without the definition that supplies its fixture. Every
/// <c>IntegrationContext</c> subclass in this project failed with "the following constructor
/// parameters did not have matching fixture data: DefaultStoreFixture fixture" for as long as the
/// project was outside CI (#5096), which is exactly as long as nobody could see it.</para>
///
/// <para>Declared in this namespace rather than <c>Marten.Testing.Harness</c> so it cannot collide
/// with the identically-named definition in the referenced assembly. Only this one is in scope for
/// the collection resolver, which is the point.</para>
///
/// <para>The repo's usual answer is to <c>&lt;Compile Include&gt;</c> the harness sources into each
/// test assembly (see the note in CompiledQueryTests.csproj). That is not available here: this
/// project references EventSourcingTests for its test types, so a second copy of
/// <c>IntegrationContext</c> would make every reference to it ambiguous.</para>
/// </summary>
[CollectionDefinition("integration")]
public class IntegrationCollection: ICollectionFixture<DefaultStoreFixture>
{
}
