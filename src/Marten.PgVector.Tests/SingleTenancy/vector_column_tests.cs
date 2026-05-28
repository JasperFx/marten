using Marten.PgVector;
using Marten.Testing.Harness;
using Pgvector;
using Shouldly;
using Xunit;

namespace Marten.PgVector.Tests.SingleTenancy;

[Collection("Marten.PgVector")]
public class vector_column_tests : IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_tests";
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;

            opts.UsePgVector();
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
    public async Task can_store_and_load_document_with_vector()
    {
        var product = new ProductWithVector
        {
            Id = Guid.NewGuid(),
            Name = "Widget",
            Embedding = new float[] { 1.0f, 2.0f, 3.0f }
        };

        await using (var session = _store.LightweightSession())
        {
            session.Store(product);
            await session.SaveChangesAsync();
        }

        await using (var query = _store.QuerySession())
        {
            var loaded = await query.LoadAsync<ProductWithVector>(product.Id);
            loaded.ShouldNotBeNull();
            loaded.Name.ShouldBe("Widget");
            loaded.Embedding.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task can_search_by_vector_similarity()
    {
        var products = new[]
        {
            new ProductWithVector { Id = Guid.NewGuid(), Name = "A", Embedding = new float[] { 1, 0, 0 } },
            new ProductWithVector { Id = Guid.NewGuid(), Name = "B", Embedding = new float[] { 0, 1, 0 } },
            new ProductWithVector { Id = Guid.NewGuid(), Name = "C", Embedding = new float[] { 0, 0, 1 } },
            new ProductWithVector { Id = Guid.NewGuid(), Name = "Near A", Embedding = new float[] { 0.9f, 0.1f, 0 } },
        };

        await using (var session = _store.LightweightSession())
        {
            foreach (var p in products) session.Store(p);
            await session.SaveChangesAsync();
        }

        // Search for vectors near [1, 0, 0] — should return "A" and "Near A" first
        var queryVector = new Vector(new float[] { 1, 0, 0 });

        await using var querySession = _store.QuerySession();
        var results = await querySession.VectorSearchAsync<ProductWithVector>(
            x => x.Embedding, queryVector, limit: 2, distance: DistanceFunction.L2);

        results.Count.ShouldBe(2);
        results[0].Name.ShouldBe("A");
        results[1].Name.ShouldBe("Near A");
    }

    [Fact]
    public async Task vector_extension_is_created()
    {
        await using var conn = _store.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM pg_extension WHERE extname = 'vector'";
        var result = await cmd.ExecuteScalarAsync();
        result.ShouldNotBeNull();
    }
}

public class ProductWithVector
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>
    /// Stored as a float array in JSONB, cast to vector() at query time.
    /// </summary>
    public float[]? Embedding { get; set; }
    public string Category { get; set; } = "";
}
