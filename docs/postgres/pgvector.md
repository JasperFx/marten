# pgvector Support

`Marten.PgVector` is an optional companion package that adds vector-similarity support to Marten on top of the [pgvector](https://github.com/pgvector/pgvector) PostgreSQL extension. It is published from the Marten repo under the MIT license and ships as the `Marten.PgVector` NuGet package.

What it gives you:

- a one-line `UsePgVector()` opt-in that registers the `vector` extension on every database Marten manages (including per-tenant databases)
- a `VectorSearchAsync` extension on `IQuerySession` that runs index-accelerated nearest-neighbor searches against a vector-typed property of a document
- a `VectorProjection` base class for event-sourced projections that maintain an embedding table alongside your stream, with content-hash skipping so unchanged content is not re-embedded
- an `IEmbeddingProvider` interface — `Marten.PgVector` is AI-model-agnostic; bring OpenAI, Ollama, a local model, or anything else

## Installation

```shell
dotnet add package Marten.PgVector
```

Your local PostgreSQL must ship the `vector` extension. The Dockerfile under `docker/postgres/Dockerfile` in this repo layers `postgresql-17-pgvector` (and `postgresql-17-postgis-3`) on the official multi-arch `postgres:17` image. In CI the [`pgvector/pgvector:pg17`](https://hub.docker.com/r/pgvector/pgvector) image is used.

## Enabling pgvector on a store

```csharp
using Marten;
using Marten.PgVector;

var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);

    // 1. Adds CREATE EXTENSION IF NOT EXISTS vector to every database
    // 2. Calls NpgsqlDataSourceBuilder.UseVector() so the Pgvector.Vector
    //    type round-trips through Npgsql
    opts.UsePgVector();

    opts.RegisterDocumentType<ProductWithVector>();
});
```

`UsePgVector()` is multi-tenant aware. The single-server-per-tenant, master-table, and sharded tenancy strategies all create the extension in each tenant database via Marten's `ExtendedSchemaObjects`, which addresses the long-standing issue of extensions only being created on the default database ([#2515](https://github.com/JasperFx/marten/issues/2515)).

## Storing vectors on a document

Put a `float[]` (or `Pgvector.Vector`) on the document. The array round-trips into the JSONB document and is cast to `vector(N)` at query time.

```csharp
public class ProductWithVector
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    // Stored as a float[] inside JSONB; cast to vector() at query time.
    public float[]? Embedding { get; set; }
    public string Category { get; set; } = "";
}
```

## Vector similarity search

`VectorSearchAsync` runs an ordered nearest-neighbor query against the chosen vector property. The three distance functions match the three pgvector index operator classes:

| `DistanceFunction` | pgvector operator | Index ops class       | Typical use               |
| ------------------ | ----------------- | --------------------- | ------------------------- |
| `L2`               | `<->`             | `vector_l2_ops`       | Euclidean distance        |
| `Cosine` (default) | `<=>`             | `vector_cosine_ops`   | Text embeddings           |
| `InnerProduct`     | `<#>`             | `vector_ip_ops`       | Inner product (negative)  |

```csharp
using Pgvector;

var queryVector = new Vector(new float[] { 1.0f, 0.0f, 0.0f });

await using var q = store.QuerySession();

var hits = await q.VectorSearchAsync<ProductWithVector>(
    x => x.Embedding,
    queryVector,
    limit: 10,
    distance: DistanceFunction.L2);
```

In conjoined multi-tenancy stores (`AllDocumentsAreMultiTenanted` + a tenant-scoped session) the search adds an automatic `tenant_id` filter so a tenant only sees its own vectors. Database-per-tenant setups are isolated at the connection level and need no extra filtering.

## Event-sourced vector projection

`VectorProjection` is a base class for projections that maintain an embedding table alongside your stream. It handles the boilerplate of mapping events to text, hashing content, calling your `IEmbeddingProvider`, and writing the embeddings — skipping the embedding API call when content has not changed.

```csharp
public record ProductCreated(Guid ProductId, string Name, string Description);
public record ProductUpdated(Guid ProductId, string Description);
public record ProductDeleted(Guid ProductId);

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
```

Register it like any other projection, and also register the projection's storage table as a schema object so Marten creates it:

```csharp
var projection = new ProductSearchProjection(myEmbeddingProvider);

var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);
    opts.UsePgVector();

    opts.Projections.Add(projection, ProjectionLifecycle.Async);
    opts.Storage.ExtendedSchemaObjects.Add(
        projection.BuildTable(opts.Events.DatabaseSchemaName));

    opts.Events.AddEventType<ProductCreated>();
    opts.Events.AddEventType<ProductUpdated>();
    opts.Events.AddEventType<ProductDeleted>();
});
```

The created table has the shape:

| Column         | Type          | Notes                                                                       |
| -------------- | ------------- | --------------------------------------------------------------------------- |
| `id`           | `uuid`        | Primary key — the projection's logical identity (defaults to `StreamId`)    |
| `embedding`    | `vector(N)`   | `N` comes from `IEmbeddingProvider.Dimensions`                              |
| `content_text` | `text`        | The source text that was embedded                                           |
| `content_hash` | `text`        | SHA-256 of `content_text` — used to skip re-embedding                       |
| `metadata`     | `jsonb`       | Reserved for caller-supplied metadata                                       |
| `last_updated` | `timestamptz` | `now()` default, refreshed on upsert                                        |

### Querying the projection table

`VectorProjectionSearchAsync` runs the canonical ordered-by-distance query against the projection table and returns the `Guid` id, distance, and the original content text:

```csharp
var results = await q.VectorProjectionSearchAsync(
    "product_search_vectors",
    myEmbeddingProvider.Embed("red running shoes"),
    limit: 10,
    distance: DistanceFunction.Cosine);

foreach (var r in results)
{
    Console.WriteLine($"{r.Id}  distance={r.Distance}  {r.ContentText}");
}
```

## Bring-your-own embeddings

`Marten.PgVector` does not ship a default embedding provider — implement `IEmbeddingProvider` against the model you want:

```csharp
public interface IEmbeddingProvider
{
    int Dimensions { get; }
    Task<Vector[]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default);
}
```

`Dimensions` must match the `vector(N)` column the projection creates. Mixing dimensions across versions is a permanent migration — `pgvector` does not let you change the column width in place.

## Notes & limitations

- `VectorSearchAsync` runs raw SQL through the session's connection — it does not go through Marten's LINQ provider or compiled-query cache. Document instances are deserialized via the store's `ISerializer`.
- The vector value lives inside the JSONB document and is cast at query time (`(d.data->>'<member>')::vector(N)`). For large tables, add an [HNSW or IVFFlat](https://github.com/pgvector/pgvector#indexing) index on that expression to keep similarity queries index-accelerated.
- Only simple member access expressions are supported in the vector property selector (`x => x.Embedding`), matching the Marten LINQ conventions.
- `VectorProjection` requires async execution — the synchronous `IProjection.Apply` overload throws.
