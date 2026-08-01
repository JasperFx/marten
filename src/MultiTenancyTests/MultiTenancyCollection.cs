using Xunit;

namespace MultiTenancyTests;

// The single definition of the "multi-tenancy" collection. The classes in this
// collection all provision real per-tenant databases against the shared test
// server, so they must never run concurrently with each other.
[CollectionDefinition("multi-tenancy", DisableParallelization = true)]
public class MultiTenancyCollection;
