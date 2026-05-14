using Xunit;
[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Compiled queries declared in LinqTests/Compiled, LinqTests/Bugs/, LinqTests/Includes/,
// LinqTests/ChildCollections/, and LinqTests/Acceptance/ go through the source-gen path
// at runtime. This attribute is the consumer-side opt-in gate for Marten.SourceGenerator.
// See #4405 for the implicit-opt-in contract.
[assembly: JasperFx.JasperFxAssembly]