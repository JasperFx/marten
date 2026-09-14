# pgvector Support

`Marten.PgVector` is an optional companion package that adds vector similarity search and hybrid (keyword plus vector) search to Marten on top of the [pgvector](https://github.com/pgvector/pgvector) PostgreSQL extension. It is published from the Marten repo under the MIT license and ships as the `Marten.PgVector` NuGet package.

What it gives you:

- a one-line `UsePgVector()` opt-in that registers the `vector` extension on every database Marten manages (including per-tenant databases)
- `VectorSearchAsync` and `VectorSearchWithScoresAsync` on `IQuerySession` for nearest-neighbor searches against an embedding stored on a document
- `VectorIndex<T>()` to declare an HNSW index that Marten creates and migrates along with the document table
- `HybridSearchAsync` and `HybridSearchWithScoresAsync`, which fuse a full text ranking and a vector ranking into one result list
- a `VectorProjection` base class for event-sourced projections that maintain an embedding table alongside your streams, with content-hash skipping so unchanged content is not re-embedded
- no embedding model of its own: implement the shared `IEmbeddingProvider` contract (the same one Polecat and Fisher take) or adapt any Microsoft.Extensions.AI embedding generator

::: tip
This page describes Marten.PgVector 9.36.0 and later. Code written against earlier versions still compiles, with obsolete warnings. See [Upgrading from the pre-9.36 API](#upgrading-from-the-pre-9-36-api).
:::

## Installation

```shell
dotnet add package Marten.PgVector
```

PostgreSQL must have the `vector` extension available. `UsePgVector()` runs `CREATE EXTENSION IF NOT EXISTS vector`, but it cannot install the extension's binaries, and the role Marten connects with needs permission to create the extension (or a DBA can create it ahead of time). Most managed PostgreSQL services offer pgvector as an allow-listed extension.

For local development and CI, the [`pgvector/pgvector`](https://hub.docker.com/r/pgvector/pgvector) images are the official PostgreSQL images with pgvector added. Marten's own CI uses `pgvector/pgvector:pg17`:

```yaml
services:
  postgres:
    image: pgvector/pgvector:pg17
    ports:
      - "5432:5432"
    environment:
      POSTGRES_PASSWORD: postgres
```

The Dockerfile under `docker/postgres/Dockerfile` in the Marten repo takes the other route and layers `postgresql-17-pgvector` (and `postgresql-17-postgis-3`) on the official multi-arch `postgres:17` image.

## Enabling pgvector on a store

<!-- snippet: sample_pgvector_use_pgvector -->
<a id='snippet-sample_pgvector_use_pgvector'></a>
```cs
_store = DocumentStore.For(opts =>
{
    opts.Connection(ConnectionSource.ConnectionString);
    opts.DatabaseSchemaName = "pgvector_tests";
    opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;

    // 1. Adds CREATE EXTENSION IF NOT EXISTS vector to every database Marten manages
    // 2. Calls NpgsqlDataSourceBuilder.UseVector() so Pgvector.Vector round-trips through Npgsql
    opts.UsePgVector();
    opts.RegisterDocumentType<ProductWithVector>();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PgVector.Tests/SingleTenancy/vector_column_tests.cs#L19-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_pgvector_use_pgvector' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`UsePgVector()` is multi-tenant aware. The single-server-per-tenant, master-table, and sharded tenancy strategies all create the extension in each tenant database via Marten's `ExtendedSchemaObjects`, which addresses the long-standing issue of extensions only being created on the default database ([#2515](https://github.com/JasperFx/marten/issues/2515)).

## Storing vectors on a document

Put a `float[]` on the document:

<!-- snippet: sample_pgvector_document_with_embedding -->
<a id='snippet-sample_pgvector_document_with_embedding'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PgVector.Tests/SingleTenancy/vector_column_tests.cs#L110-L122' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_pgvector_document_with_embedding' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The array is stored inside the JSONB document like any other member. Searches cast it at query time as `(data ->> 'Embedding')::vector(N)`, where `N` is the length of the query vector, so every embedding stored on that member must have the same length. Documents whose embedding is null are skipped by searches. The JSON key follows your serializer's casing, so a camelCase store reads `'embedding'`.

## Bring your own embeddings

`Marten.PgVector` does not ship an embedding model. Embeddings come from an `IEmbeddingProvider`, which lives in the `JasperFx.Events.Vectors` namespace of the `JasperFx.Events` package that Marten already references:

```csharp
namespace JasperFx.Events.Vectors;

public interface IEmbeddingProvider
{
    int Dimensions { get; }

    Task<ReadOnlyMemory<float>[]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default);
}
```

The contract a provider has to keep:

- one vector per input text, in input order, each exactly `Dimensions` long
- an empty `texts` array returns an empty result without calling the model

For the query side, the `GenerateEmbeddingAsync(text)` extension method embeds a single string. Because this is the same contract Polecat and Fisher take, a provider written once works with all three stores.

`Dimensions` sizes the `vector(N)` column a `VectorProjection` creates, and it has to match the `dimensions` you give `VectorIndex`. Switching to a model with a different dimension count means re-embedding everything you have stored.

### Microsoft.Extensions.AI

If you already use [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) for OpenAI, Azure OpenAI, Ollama, or another model, the `JasperFx.Events.MicrosoftExtensionsAI` package adapts an `IEmbeddingGenerator<string, Embedding<float>>` to `IEmbeddingProvider`:

```shell
dotnet add package JasperFx.Events.MicrosoftExtensionsAI
```

```csharp
using JasperFx.Events.MicrosoftExtensionsAI;
using JasperFx.Events.Vectors;
using Microsoft.Extensions.AI;

IEmbeddingGenerator<string, Embedding<float>> generator = /* from your M.E.AI provider package */;

IEmbeddingProvider provider = generator.AsEmbeddingProvider(dimensions: 1536);
```

`dimensions` is optional. Without it, the adapter uses `EmbeddingGenerationOptions.Dimensions` if you pass options, then the generator's own `DefaultModelDimensions` metadata, and throws if none of them supplies a number. Stores size their columns from it before any text is embedded, so it cannot be discovered lazily. On every call the adapter checks that the generator returned one vector per text, each of the declared length, and throws with the model named if not. Options you pass are forwarded as-is; nothing is derived from `dimensions`.

## Vector similarity search

`VectorSearchAsync<T>` returns the `limit` documents nearest to a query vector, closest first. `VectorSearchWithScoresAsync<T>` runs the same query and returns each document with its distance as a `VectorMatch<T>(T Document, double Distance)`:

```csharp
public static Task<IReadOnlyList<VectorMatch<T>>> VectorSearchWithScoresAsync<T>(
    this IQuerySession session,
    Expression<Func<T, object?>> vectorProperty,
    ReadOnlyMemory<float> queryVector,
    int limit = 10,
    DistanceFunction distance = DistanceFunction.Cosine) where T : class
```

<!-- snippet: sample_pgvector_vector_search -->
<a id='snippet-sample_pgvector_vector_search'></a>
```cs
await using var query = _store.QuerySession();

// Query is a ReadOnlyMemory<float>, which is what IEmbeddingProvider hands back.
// Each VectorMatch<T> carries the document and its distance, smallest first.
var scored = await query.VectorSearchWithScoresAsync<NeutralDoc>(x => x.Embedding, Query);

// Just the documents, in the same order
var plain = await query.VectorSearchAsync<NeutralDoc>(x => x.Embedding, Query);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PgVector.Tests/SingleTenancy/neutral_contract_search.cs#L90-L99' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_pgvector_vector_search' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In application code the query vector usually comes straight from your provider, and the distance lets you apply a cutoff:

```csharp
var queryVector = await provider.GenerateEmbeddingAsync("red running shoes");

var matches = await session.VectorSearchWithScoresAsync<ProductWithVector>(
    x => x.Embedding, queryVector, limit: 10);

// Smaller is closer. Pick the cutoff for your own model and data.
var closeEnough = matches.Where(m => m.Distance < 0.3).Select(m => m.Document).ToList();
```

An overload of `VectorSearchAsync` that takes a `Pgvector.Vector` is kept for existing call sites.

Without a [vector index](#hnsw-index), every search is an exact sequential scan that computes the distance for every row. That's fine for thousands of documents and slow for millions.

### Distance functions

`DistanceFunction` is `JasperFx.Events.Vectors.DistanceFunction`, shared with Polecat and Fisher. **Every member is a distance, so smaller is closer, including inner product.** pgvector's `<#>` operator returns the *negative* inner product for exactly that reason, so results always come back in ascending order.

| `DistanceFunction` | pgvector operator | Index operator class | What `Distance` holds                               |
| ------------------ | ----------------- | -------------------- | --------------------------------------------------- |
| `Cosine` (default) | `<=>`             | `vector_cosine_ops`  | `1 - cosine similarity`, from 0 (identical) to 2    |
| `L2`               | `<->`             | `vector_l2_ops`      | Euclidean distance, 0 or more                       |
| `InnerProduct`     | `<#>`             | `vector_ip_ops`      | The negative inner product; more negative is closer |

Use the metric your embedding model was trained for. For most text embedding models that is cosine. Inner product gives the same ranking as cosine for unit-length vectors and is cheaper to compute, but it only ranks meaningfully when the vectors are normalized.

## HNSW index

`VectorIndex<T>` declares an [HNSW](https://github.com/pgvector/pgvector#hnsw) index over a document's embedding, so searches become index scans instead of sequential scans:

```csharp
public static StoreOptions VectorIndex<T>(
    this StoreOptions opts,
    Expression<Func<T, object?>> vectorProperty,
    int dimensions,
    DistanceFunction distance = DistanceFunction.Cosine,
    int? m = null,
    int? efConstruction = null)
```

<!-- snippet: sample_pgvector_vector_index -->
<a id='snippet-sample_pgvector_vector_index'></a>
```cs
// An HNSW index over (data ->> 'Embedding')::vector(3) with vector_cosine_ops.
// distance defaults to Cosine; m and efConstruction default to pgvector's own defaults.
opts.VectorIndex<IndexedDoc>(x => x.Embedding, dimensions: 3, m: 16, efConstruction: 64);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PgVector.Tests/SingleTenancy/vector_index_tests.cs#L45-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_pgvector_vector_index' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

That declaration produces this index:

```sql
CREATE INDEX idx_mt_doc_indexeddoc_embedding_cosine ON pgvector_index_tests.mt_doc_indexeddoc
    USING hnsw ((((data ->> 'Embedding'::text))::vector(3)) vector_cosine_ops)
    WITH (m='16', ef_construction='64')
```

The index belongs to the document's mapping, so it is created with the table, picked up by Marten's schema migrations like any other index, and dropped with the table. Omit `m` and `efConstruction` to use pgvector's defaults (16 and 64). A higher `efConstruction` builds more slowly and gives better recall.

Three things have to line up for PostgreSQL to use the index. None of them raises an error when they don't: the query just stays a sequential scan.

- **One index serves one metric.** A `vector_cosine_ops` index is not used by an L2 or inner product search. The index name includes the metric, so declare one index per metric you actually search by:

  ```csharp
  opts.VectorIndex<ProductWithVector>(x => x.Embedding, dimensions: 1536);
  opts.VectorIndex<ProductWithVector>(x => x.Embedding, dimensions: 1536,
      distance: DistanceFunction.InnerProduct);
  ```

- **`dimensions` must equal the query vector's length.** The search casts to `vector(N)` using the query vector's length, and that cast is part of the indexed expression.
- **The member must be the one you search.** The indexed expression is built from the same member path and serializer casing that `VectorSearchAsync` uses, so declaring it through `VectorIndex` keeps the two in step.

::: warning
HNSW is approximate, and pgvector caps how many rows one index scan returns with the `hnsw.ef_search` setting, which defaults to 40. With an index in place, `VectorSearchAsync(..., limit: 100)` returns at most 40 documents, and a tenant filter in a conjoined store is applied after that cap, so it can return fewer. Marten does not change the setting. Raise it on the connection, for example with `Options=-c hnsw.ef_search=100` in the Npgsql connection string, or on pgvector 0.8 and later enable iterative scans with `-c hnsw.iterative_scan=relaxed_order`. See [pgvector's query options](https://github.com/pgvector/pgvector#query-options).
:::

## Hybrid search

Keyword search and vector search fail in different directions. Full text search misses a paraphrase that shares no words with the query. Vector search misses an exact identifier, a product code, or a rare name the embedding model never learned. `HybridSearchAsync<T>` runs both searches and fuses their rankings into one list.

The text half needs a full text index on the document, and the vector half benefits from a [vector index](#hnsw-index):

<!-- snippet: sample_pgvector_hybrid_search_setup -->
<a id='snippet-sample_pgvector_hybrid_search_setup'></a>
```cs
_store = DocumentStore.For(opts =>
{
    opts.Connection(ConnectionSource.ConnectionString);
    opts.DatabaseSchemaName = "pgvector_hybrid_tests";
    opts.AutoCreateSchemaObjects = AutoCreate.All;
    opts.UsePgVector();

    // The text leg searches through this index. Without one it still works,
    // but over the whole document and without an index.
    opts.Schema.For<Article>().FullTextIndex();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PgVector.Tests/SingleTenancy/hybrid_search_tests.cs#L33-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_pgvector_hybrid_search_setup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample_pgvector_hybrid_search -->
<a id='snippet-sample_pgvector_hybrid_search'></a>
```cs
// The search text feeds the full-text leg, the query vector feeds the vector leg,
// and the two rankings are fused with reciprocal rank fusion
var results = await query.HybridSearchAsync<Article>(
    x => x.Embedding, "quantum", Query,
    limit: 2,
    options: new HybridSearchOptions(CandidateDepth: 2));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PgVector.Tests/SingleTenancy/hybrid_search_tests.cs#L98-L105' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_pgvector_hybrid_search' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`HybridSearchWithScoresAsync<T>` returns each document with its fused score as a `HybridMatch<T>(T Document, double Score)`:

<!-- snippet: sample_pgvector_hybrid_search_with_scores -->
<a id='snippet-sample_pgvector_hybrid_search_with_scores'></a>
```cs
// Each HybridMatch<T> carries the document and its fused score. Larger is better.
var scored = await query.HybridSearchWithScoresAsync<Article>(x => x.Embedding, "quantum", Query);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PgVector.Tests/SingleTenancy/hybrid_search_tests.cs#L136-L139' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_pgvector_hybrid_search_with_scores' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

```csharp
public static Task<IReadOnlyList<HybridMatch<T>>> HybridSearchWithScoresAsync<T>(
    this IQuerySession session,
    Expression<Func<T, object?>> vectorProperty,
    string searchText,
    ReadOnlyMemory<float> queryVector,
    int limit = 10,
    HybridSearchOptions? options = null,
    CancellationToken token = default) where T : class
```

In an application, the search text and the query vector are usually the same input:

```csharp
var text = "red running shoes";
var queryVector = await provider.GenerateEmbeddingAsync(text);

var results = await session.HybridSearchAsync<Product>(x => x.Embedding, text, queryVector, limit: 20);
```

### Fusion

Hybrid search uses reciprocal rank fusion (RRF). Each document scores `1 / (K + rank)` from every search that found it, with 1-based ranks, and the scores are summed:

- **Only rank counts.** `ts_rank` values and vector distances are on unrelated scales and don't even run in the same direction, so they are never compared or normalized. There are no per-search weights to tune, and changing the embedding model or text configuration doesn't need recalibrating.
- **The union is fused.** A document found by only one of the two searches still scores. That is the point: what keyword search alone finds is exactly what vector search is bad at.
- **Larger is better.** `HybridMatch<T>.Score` runs the opposite way from `VectorMatch<T>.Distance`. The absolute value is small (at most `2 / (K + 1)`, about 0.033 with the default `K`) and is only useful for comparing results of the same query or applying a floor.
- **Ties are broken deterministically**: by score, then by each document's best rank in either search, then by id, so the same query returns the same page every time.
- **It is two statements, not one.** The text search and the vector search are separate queries, each reading `CandidateDepth` rows, and they are fused in memory. There is no offset or paging. The vector half is `VectorSearchWithScoresAsync`, so it uses a `VectorIndex` built for `options.Distance`, and it is subject to the `hnsw.ef_search` cap described [above](#hnsw-index).

### Options

`HybridSearchOptions` is a record, so pass only what you want to change:

```csharp
var options = new HybridSearchOptions(CandidateDepth: 100, TextStyle: HybridTextStyle.WebStyle);
```

| Option           | Default                     | Meaning                                                                                                                                                                |
| ---------------- | --------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `K`              | `60`                        | The RRF smoothing constant. Must be at least 1. Lowering it gives a first-place finish more weight.                                                                    |
| `CandidateDepth` | `max(limit × 4, 50)`        | How many rows each search reads before fusing. Must be at least `limit`. A document ranked 40th by one search and 1st by the other is the result hybrid search is for. |
| `Distance`       | `DistanceFunction.Cosine`   | The metric for the vector search. Match it to your `VectorIndex`.                                                                                                      |
| `TextStyle`      | `HybridTextStyle.PlainText` | `PlainText` uses `plainto_tsquery` (every word, no syntax). `WebStyle` uses `websearch_to_tsquery` (quoted phrases, `or`, a leading `-` to exclude).                   |
| `RegConfig`      | `"english"`                 | The PostgreSQL text search configuration. It also selects which full text index the text search uses.                                                                  |

Both text styles are safe to feed raw search-box input. Raw `to_tsquery` syntax is deliberately not offered, because a malformed query would fail the whole fused search.

### The text leg

The text search runs `WHERE <tsvector> @@ <tsquery> ORDER BY ts_rank(<tsvector>, <tsquery>) DESC`. It picks the `tsvector` the same way Marten's LINQ [full text search](/documents/full-text) does, by looking for a full text index on the document registered with the same `RegConfig`:

- **One matching index.** The search uses that index's expression, so its GIN index serves the match. [Weighted indexes](/documents/full-text#weighted-full-text-indexes-and-relevance-ranking) are included, so a title match outranks a body match. GIN indexes cannot order, so `ts_rank` still sorts every matched row.
- **No matching index.** The search falls back to `to_tsvector('<RegConfig>'::regconfig, data)` over the *whole* JSON document, computed row by row. That is a sequential scan that matches every string value in the document. It works on small tables and gets expensive fast on large ones. An index registered with a different `regConfig` (for example `FullTextIndex("simple")`) is not used unless you pass that value as `RegConfig`.
- **Two or more matching indexes.** Marten can't tell which one you mean and throws `AmbiguousFullTextIndexException`. Give the indexes different `regConfig` values and pass the one you want, or register a single index covering every member you need.

## Event-sourced vector projection

`VectorProjection` is a base class for projections that maintain an embedding table alongside your streams. It maps events to text, hashes the text, calls your `IEmbeddingProvider` for anything new or changed, and writes the embeddings. When content hasn't changed, it skips the call to the model.

<!-- snippet: sample_pgvector_vector_projection -->
<a id='snippet-sample_pgvector_vector_projection'></a>
```cs
public record ProductCreated(Guid ProductId, string Name, string Description);
public record ProductUpdated(Guid ProductId, string Description);
public record ProductDeleted(Guid ProductId);

public class ProductSearchProjection : VectorProjection
{
    public ProductSearchProjection(Neutral.IEmbeddingProvider provider)
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

        // The rows are keyed on the product, so the delete has to be too. Passing no selector here
        // deletes by stream id, which is a row this projection never wrote.
        map.Delete<ProductDeleted>(e => e.ProductId);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PgVector.Tests/SingleTenancy/vector_projection_tests.cs#L15-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_pgvector_vector_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

- `Map<TEvent>(contentSelector, idSelector)` turns an event into the text to embed. The id selector returns the row's `Guid` id; without one, the event's stream id is used.
- `Delete<TEvent>(idSelector)` removes the row. **If any `Map` call keys rows on a member of the event, every `Delete` call has to pass a selector too.** A delete without one would address the stream id, a row that was never written, so the constructor throws an `InvalidOperationException` naming the event types instead.
- A content selector that throws fails the batch like any other broken projection. It is not treated as "no content".

Register it like any other projection, and also register the projection's storage table as a schema object so Marten creates it:

<!-- snippet: sample_pgvector_register_vector_projection -->
<a id='snippet-sample_pgvector_register_vector_projection'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PgVector.Tests/SingleTenancy/vector_projection_tests.cs#L58-L78' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_pgvector_register_vector_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Prefer `ProjectionLifecycle.Async` in production. An inline vector projection adds a network round trip to your embedding model to every `SaveChangesAsync` that appends a mapped event.

The created table has this shape:

| Column         | Type          | Notes                                                  |
| -------------- | ------------- | ------------------------------------------------------ |
| `id`           | `uuid`        | Primary key: the id selector's value, or the stream id |
| `embedding`    | `vector(N)`   | `N` comes from `IEmbeddingProvider.Dimensions`         |
| `content_text` | `text`        | The source text that was embedded                      |
| `content_hash` | `text`        | SHA-256 of `content_text`, used to skip re-embedding   |
| `metadata`     | `jsonb`       | Reserved; not populated                                |
| `last_updated` | `timestamptz` | `now()` default, refreshed on upsert                   |

### Querying the projection table

`VectorProjectionSearchAsync` runs an ordered-by-distance query against the projection table and returns each row's `Guid` id, distance, and original content text. It takes a `Pgvector.Vector` rather than `ReadOnlyMemory<float>`, so wrap your provider's embedding in `new Vector(...)`:

<!-- snippet: sample_pgvector_vector_projection_search -->
<a id='snippet-sample_pgvector_vector_projection_search'></a>
```cs
// The shared IEmbeddingProvider returns ReadOnlyMemory<float>, but
// VectorProjectionSearchAsync takes a Pgvector.Vector, so wrap the embedding
var queryEmbedding = await _embedder.GenerateEmbeddingAsync(
    "Widget A fantastic widget for all purposes", TestContext.Current.CancellationToken);

var results = await session.VectorProjectionSearchAsync(
    "product_search_vectors",
    new Vector(queryEmbedding),
    limit: 10,
    distance: Neutral.DistanceFunction.L2);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PgVector.Tests/SingleTenancy/vector_projection_tests.cs#L100-L111' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_pgvector_vector_projection_search' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The table name is unqualified; Marten looks for it in the event store's schema. Results are `VectorSearchResult` objects (`Id`, a `float` `Distance`, and `ContentText`), not documents, so load whatever the ids refer to yourself. `VectorIndex` applies to document tables only, and the projection table has no vector index, so these searches are sequential scans.

### Projection limitations

- **`Guid` ids only.** The table's primary key is a `uuid`, and id selectors return `Guid`. Streams keyed by string identity are not supported.
- **No conjoined tenancy.** The table has no `tenant_id` column, and `VectorProjectionSearchAsync` doesn't filter by tenant, so in a conjoined store every tenant's rows share one table and a search returns all of them. Database-per-tenant works: rows are written to the database the events came from, and searches read from the session's database.
- **Async execution.** The synchronous `IProjection.Apply` overload throws; the projection only runs through its async path.

## Multi-tenancy

| API                                                | Conjoined tenancy (one database)               | Database per tenant    |
| -------------------------------------------------- | ---------------------------------------------- | ---------------------- |
| `VectorSearchAsync`, `VectorSearchWithScoresAsync` | Filtered to the session's tenant               | Isolated by connection |
| `HybridSearchAsync`, `HybridSearchWithScoresAsync` | Both searches filtered to the session's tenant | Isolated by connection |
| `VectorProjection`, `VectorProjectionSearchAsync`  | Not supported                                  | Supported              |

In a single-database store (`AllDocumentsAreMultiTenanted` plus a tenant-scoped session), document searches add a `tenant_id` filter whenever the session's tenant isn't the default tenant. Database-per-tenant setups are isolated at the connection level and need no extra filtering.

## Other Critter Stack stores

`IEmbeddingProvider`, `DistanceFunction`, and `VectorMatch<T>` are shared with the other Critter Stack document stores, so embedding code and metric choices carry across. `HybridSearchOptions` and `HybridMatch<T>` are Marten.PgVector's own types. Each store documents its own details and limits:

- Polecat (SQL Server 2025): [full text search](https://polecat.jasperfx.net/documents/querying/full-text-search), [vector search](https://polecat.jasperfx.net/documents/querying/vector-search), [hybrid search](https://polecat.jasperfx.net/documents/querying/hybrid-search)
- Fisher (SQLite): [full text search](https://fisher.jasperfx.net/documents/querying/linq/full-text), [vector search](https://fisher.jasperfx.net/documents/querying/vector-search), [hybrid search](https://fisher.jasperfx.net/documents/querying/hybrid-search), [vector projections](https://fisher.jasperfx.net/events/projections/vector)

## Upgrading from the pre-9.36 API {#upgrading-from-the-pre-9-36-api}

9.36 moved Marten.PgVector onto the store-neutral contracts in `JasperFx.Events.Vectors`. The earlier API is still there, marked `[Obsolete]`, so existing code keeps compiling and working, with CS0618 warnings (errors if you build with `TreatWarningsAsErrors`).

| Pre-9.36                                                                       | Replacement                                                                             | Notes                                                                                                                  |
| ------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `Marten.PgVector.DistanceFunction`                                             | `JasperFx.Events.Vectors.DistanceFunction`                                              | Same member names, **different numeric values**. Convert by name with `ToNeutral()`, never with a cast.                |
| `Marten.PgVector.Projection.IEmbeddingProvider`, returning `Task<Vector[]>`    | `JasperFx.Events.Vectors.IEmbeddingProvider`, returning `Task<ReadOnlyMemory<float>[]>` | `VectorProjection` still accepts a legacy provider and adapts it. When porting, return `vector.Memory` or `float[]`s.  |
| `VectorSearchAsync(x => ..., Vector, limit, Marten.PgVector.DistanceFunction)` | `VectorSearchAsync(x => ..., ReadOnlyMemory<float>, limit, distance)`                   | The `Vector` overload that takes the shared enum is not obsolete. `Vector.Memory` gives you a `ReadOnlyMemory<float>`. |
| `PgVectorOptions.VectorOn<T>()`                                                | `StoreOptions.VectorIndex<T>()`                                                         | `VectorOn` never had any effect. `VectorIndex` declares a real HNSW index.                                             |

The numeric values matter because the legacy enum declares `L2` first and the shared one declares `Cosine` first. Casting one to the other would silently turn an L2 search into a cosine search.

### Fixing CS0104

A file that imports both the old and the new namespaces gets an ambiguous reference on the bare type names:

```text
error CS0104: 'DistanceFunction' is an ambiguous reference between 'Marten.PgVector.DistanceFunction' and 'JasperFx.Events.Vectors.DistanceFunction'
error CS0104: 'IEmbeddingProvider' is an ambiguous reference between 'Marten.PgVector.Projection.IEmbeddingProvider' and 'JasperFx.Events.Vectors.IEmbeddingProvider'
```

`DistanceFunction` clashes when you import `Marten.PgVector` and `JasperFx.Events.Vectors`. `IEmbeddingProvider` clashes when you import `Marten.PgVector.Projection` and `JasperFx.Events.Vectors`. Alias the shared types at the top of the file, or qualify them where you use them:

```csharp
using JasperFx.Events.Vectors;
using Marten.PgVector;
using Marten.PgVector.Projection;

using DistanceFunction = JasperFx.Events.Vectors.DistanceFunction;
using IEmbeddingProvider = JasperFx.Events.Vectors.IEmbeddingProvider;
```

Files that import only `Marten.PgVector` are unaffected; they keep binding to the legacy types.

## Notes & limitations

- The search methods run raw SQL on their own connection to the session's database. They don't go through Marten's LINQ provider or compiled-query cache, can't be combined with `Where` clauses, and don't see changes a session hasn't committed yet. Documents are deserialized with the store's serializer and are not tracked by the session.
- Only simple member access expressions are supported in the vector property selector (`x => x.Embedding`), matching the Marten LINQ conventions.
- `VectorSearchAsync` and `VectorSearchWithScoresAsync` don't take a `CancellationToken`.
