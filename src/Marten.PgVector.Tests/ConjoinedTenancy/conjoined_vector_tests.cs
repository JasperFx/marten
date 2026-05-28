using Marten.Storage;
using Marten.PgVector;
using Marten.PgVector.Tests.SingleTenancy;
using Marten.Testing.Harness;
using Pgvector;
using Shouldly;
using Xunit;

namespace Marten.PgVector.Tests.ConjoinedTenancy;

[Collection("Marten.PgVector")]
public class conjoined_vector_tests : IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_conjoined";
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;

            opts.UsePgVector();

            // Enable conjoined multi-tenancy
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;

            opts.RegisterDocumentType<ProductWithVector>();
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task can_store_and_query_vectors_per_tenant()
    {
        // Store vectors for tenant A
        await using (var session = _store.LightweightSession("tenant_a"))
        {
            session.Store(new ProductWithVector
            {
                Id = Guid.NewGuid(),
                Name = "Tenant A Widget",
                Embedding = new float[] { 1, 0, 0 }
            });
            await session.SaveChangesAsync();
        }

        // Store vectors for tenant B
        await using (var session = _store.LightweightSession("tenant_b"))
        {
            session.Store(new ProductWithVector
            {
                Id = Guid.NewGuid(),
                Name = "Tenant B Widget",
                Embedding = new float[] { 0, 1, 0 }
            });
            await session.SaveChangesAsync();
        }

        // Query tenant A — should only see tenant A's data
        await using (var q = _store.QuerySession("tenant_a"))
        {
            var results = await q.Query<ProductWithVector>().ToListAsync();
            results.Count.ShouldBe(1);
            results[0].Name.ShouldBe("Tenant A Widget");
        }

        // Query tenant B — should only see tenant B's data
        await using (var q = _store.QuerySession("tenant_b"))
        {
            var results = await q.Query<ProductWithVector>().ToListAsync();
            results.Count.ShouldBe(1);
            results[0].Name.ShouldBe("Tenant B Widget");
        }
    }

    [Fact]
    public async Task vector_search_respects_tenant_isolation()
    {
        // Store different vectors per tenant
        await using (var session = _store.LightweightSession("search_a"))
        {
            session.Store(new ProductWithVector
            {
                Id = Guid.NewGuid(), Name = "A-Close",
                Embedding = new float[] { 0.9f, 0.1f, 0 }
            });
            session.Store(new ProductWithVector
            {
                Id = Guid.NewGuid(), Name = "A-Far",
                Embedding = new float[] { 0, 0, 1 }
            });
            await session.SaveChangesAsync();
        }

        await using (var session = _store.LightweightSession("search_b"))
        {
            session.Store(new ProductWithVector
            {
                Id = Guid.NewGuid(), Name = "B-Close",
                Embedding = new float[] { 0.95f, 0.05f, 0 }
            });
            await session.SaveChangesAsync();
        }

        // Search tenant A for vectors near [1, 0, 0]
        var queryVector = new Vector(new float[] { 1, 0, 0 });

        await using var qa = _store.QuerySession("search_a");
        var resultsA = await qa.VectorSearchAsync<ProductWithVector>(
            x => x.Embedding, queryVector, limit: 10, distance: DistanceFunction.L2);

        // Should only find tenant A's documents
        resultsA.Count.ShouldBe(2);
        resultsA.ShouldAllBe(r => r.Name.StartsWith("A-"));

        // Search tenant B
        await using var qb = _store.QuerySession("search_b");
        var resultsB = await qb.VectorSearchAsync<ProductWithVector>(
            x => x.Embedding, queryVector, limit: 10, distance: DistanceFunction.L2);

        resultsB.Count.ShouldBe(1);
        resultsB[0].Name.ShouldBe("B-Close");
    }
}
