using JasperFx.Events.Projections;
using Marten.PgVector;
using Marten.PgVector.Projection;
using Marten.PgVector.Tests.Helpers;
using Marten.Testing.Harness;
using Pgvector;
using Shouldly;
using Xunit;

namespace Marten.PgVector.Tests.SingleTenancy;

#region Test Events

public record ProductCreated(Guid ProductId, string Name, string Description);
public record ProductUpdated(Guid ProductId, string Description);
public record ProductDeleted(Guid ProductId);

#endregion

#region Test Projection

public class ProductSearchProjection : VectorProjection
{
    public ProductSearchProjection(IEmbeddingProvider provider)
        : base("product_search_vectors", provider)
    {
    }

    protected override void Configure(VectorProjectionMapping map)
    {
        map.Map<ProductCreated>(
            e => $"{e.Name} {e.Description}",
            e => e.ProductId);

        map.Map<ProductUpdated>(
            e => e.Description,
            e => e.ProductId);

        map.Delete<ProductDeleted>();
    }
}

#endregion

public class vector_projection_tests : IAsyncLifetime
{
    private DocumentStore _store = null!;
    private FakeEmbeddingProvider _embedder = null!;

    public async Task InitializeAsync()
    {
        _embedder = new FakeEmbeddingProvider(dimensions: 3);

        var projection = new ProductSearchProjection(_embedder);

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_proj_tests";
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;

            opts.UsePgVector();

            // Register the projection as Inline for simpler testing
            // (In production, use Async lifecycle with the daemon)
            opts.Projections.Add(projection, ProjectionLifecycle.Inline);

            // Register the projection's table as a schema object
            opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable("pgvector_proj_tests"));

            opts.Events.AddEventType<ProductCreated>();
            opts.Events.AddEventType<ProductUpdated>();
            opts.Events.AddEventType<ProductDeleted>();
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
    public async Task creates_embedding_on_event_append()
    {
        var productId = Guid.NewGuid();

        await using var session = _store.LightweightSession();
        session.Events.StartStream(productId,
            new ProductCreated(productId, "Widget", "A fantastic widget for all purposes"));
        await session.SaveChangesAsync();

        // Query the projection table directly
        var results = await session.VectorProjectionSearchAsync(
            "product_search_vectors",
            _embedder.GenerateVector("Widget A fantastic widget for all purposes"),
            limit: 10,
            distance: DistanceFunction.L2);

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe(productId);
        results[0].ContentText.ShouldBe("Widget A fantastic widget for all purposes");
    }

    [Fact]
    public async Task updates_embedding_when_content_changes()
    {
        var productId = Guid.NewGuid();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(productId,
                new ProductCreated(productId, "Widget", "Original description"));
            await session.SaveChangesAsync();
        }

        // Update the product
        await using (var session = _store.LightweightSession())
        {
            session.Events.Append(productId,
                new ProductUpdated(productId, "Updated description"));
            await session.SaveChangesAsync();
        }

        // Search should find the updated content
        await using var querySession = _store.QuerySession();
        var results = await querySession.VectorProjectionSearchAsync(
            "product_search_vectors",
            _embedder.GenerateVector("Updated description"),
            limit: 10,
            distance: DistanceFunction.L2);

        results.Count.ShouldBe(1);
        results[0].ContentText.ShouldBe("Updated description");
    }

    [Fact]
    public async Task skips_re_embedding_when_content_unchanged()
    {
        var productId = Guid.NewGuid();
        var callCount = 0;
        var countingEmbedder = new CallCountingEmbeddingProvider(_embedder, () => callCount++);

        var projection = new ProductSearchProjection(countingEmbedder);
        var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_hash_tests";
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
            opts.UsePgVector();
            opts.Projections.Add(projection, ProjectionLifecycle.Inline);
            opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable("pgvector_hash_tests"));
            opts.Events.AddEventType<ProductCreated>();
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // First append — should call embedder
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(productId,
                new ProductCreated(productId, "Widget", "Same content"));
            await session.SaveChangesAsync();
        }

        var firstCallCount = callCount;
        firstCallCount.ShouldBeGreaterThan(0);

        // Append same content again — embedder should NOT be called
        await using (var session = store.LightweightSession())
        {
            session.Events.Append(productId,
                new ProductCreated(productId, "Widget", "Same content"));
            await session.SaveChangesAsync();
        }

        callCount.ShouldBe(firstCallCount); // No additional calls

        store.Dispose();
    }

    [Fact]
    public async Task deletes_embedding_on_delete_event()
    {
        var productId = Guid.NewGuid();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(productId,
                new ProductCreated(productId, "Widget", "To be deleted"));
            await session.SaveChangesAsync();
        }

        // Delete
        await using (var session = _store.LightweightSession())
        {
            session.Events.Append(productId,
                new ProductDeleted(productId));
            await session.SaveChangesAsync();
        }

        // Search should find nothing
        await using var querySession = _store.QuerySession();
        var results = await querySession.VectorProjectionSearchAsync(
            "product_search_vectors",
            _embedder.GenerateVector("anything"),
            limit: 10,
            distance: DistanceFunction.L2);

        results.Count.ShouldBe(0);
    }

    [Fact]
    public async Task multiple_products_searchable_by_similarity()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        await using var session = _store.LightweightSession();
        session.Events.StartStream(id1,
            new ProductCreated(id1, "Red Shoes", "Bright red running shoes"));
        session.Events.StartStream(id2,
            new ProductCreated(id2, "Blue Shoes", "Navy blue casual shoes"));
        session.Events.StartStream(id3,
            new ProductCreated(id3, "Garden Hose", "50ft expandable garden hose"));
        await session.SaveChangesAsync();

        // Search — should return all 3 ordered by distance
        await using var querySession = _store.QuerySession();
        var results = await querySession.VectorProjectionSearchAsync(
            "product_search_vectors",
            _embedder.GenerateVector("Red Shoes Bright red running shoes"),
            limit: 10,
            distance: DistanceFunction.L2);

        results.Count.ShouldBe(3);
        // First result should be exact match
        results[0].Id.ShouldBe(id1);
        results[0].Distance.ShouldBe(0f); // Exact match = 0 distance
    }
}

/// <summary>
/// Wrapper that counts embedding API calls for testing content hash skipping.
/// </summary>
internal class CallCountingEmbeddingProvider : IEmbeddingProvider
{
    private readonly IEmbeddingProvider _inner;
    private readonly Action _onCall;

    public CallCountingEmbeddingProvider(IEmbeddingProvider inner, Action onCall)
    {
        _inner = inner;
        _onCall = onCall;
    }

    public int Dimensions => _inner.Dimensions;

    public Task<Vector[]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default)
    {
        _onCall();
        return _inner.GenerateEmbeddingsAsync(texts, ct);
    }
}
