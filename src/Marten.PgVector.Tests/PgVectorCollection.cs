using Xunit;

namespace Marten.PgVector.Tests;

/// <summary>
/// Forces all `Marten.PgVector` test classes into a single xUnit collection so they run
/// sequentially. Each test class spins up its own DocumentStore that calls
/// `UsePgVector()`, which registers a `CREATE EXTENSION IF NOT EXISTS vector` schema
/// object. PostgreSQL's `CREATE EXTENSION IF NOT EXISTS` is not race-safe — concurrent
/// callers can both pass the existence check before either has inserted into
/// `pg_extension`, and the loser hits `23505 duplicate key` on
/// `pg_extension_name_index`. xUnit's default puts each test class in its own
/// auto-collection (parallel), so without this we'd race against ourselves.
/// </summary>
[CollectionDefinition("Marten.PgVector", DisableParallelization = true)]
public class PgVectorCollection;
