# pgvector Support

`Marten.PgVector` is an optional companion package that adds vector similarity search and hybrid (keyword plus vector) search to Marten on top of the [pgvector](https://github.com/pgvector/pgvector) PostgreSQL extension. It is published from the Marten repo under the MIT license and ships as the `Marten.PgVector` NuGet package.

What it gives you:

- a one-line `UsePgVector()` opt-in that registers the `vector` extension on every database Marten manages (including per-tenant databases)
- `VectorSearchAsync` and `VectorSearchWithScoresAsync` on `IQuerySession` for nearest-neighbor searches against an embedding stored on a document
- a store-neutral `session.Search` accessor carrying the same two searches plus an optional LINQ `filter`, so retrieval code can be written without naming Marten
- `VectorIndex<T>()` to declare an HNSW index that Marten creates and migrates along with the document table
- `HybridSearchAsync` and `HybridSearchWithScoresAsync`, which fuse a full text ranking and a vector ranking into one result list
- a `VectorProjection<TId>` base class for event-sourced projections that maintain an embedding table alongside your streams, with content-hash skipping so unchanged content is not re-embedded
- no embedding model of its own: implement the shared `IEmbeddingProvider` contract (the same one Polecat and Fisher take) or adapt any Microsoft.Extensions.AI embedding generator

::: tip
This page describes Marten.PgVector 9.37.0 and later. Code written against earlier versions still compiles, with obsolete warnings. See [Upgrading from the pre-9.36 API](#upgrading-from-the-pre-9-36-api) and [What changed in 9.37](#what-changed-in-9-37).
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

### What a search filters for you

Both searches build their `WHERE` through the document's own Marten storage, so they apply exactly the predicates `Query<T>()` would: conjoined tenancy, a document hierarchy's discriminator, and — since 9.37 — **soft deletes**.

They also read each row back through that storage, the way `Query<T>()` does. A search for a hierarchy's base type returns every match as its **concrete subtype**, with its subclass members populated, and a search for a subclass returns only that subclass. Before [#5440](https://github.com/JasperFx/marten/issues/5440), a base-type search deserialized every row as the base type.

::: warning
Before 9.37 neither search had a soft-delete predicate, so a vector or hybrid search over a document type configured with `SoftDeleted()` returned deleted documents while the same code on Polecat and Fisher did not. If your application was filtering those out itself, that filter is now redundant rather than wrong.
:::

Use `MaybeDeleted()` in a [`filter`](#store-neutral-search) if you genuinely want deleted documents back.

### Distance functions

`DistanceFunction` is `JasperFx.Events.Vectors.DistanceFunction`, shared with Polecat and Fisher. **Every member is a distance, so smaller is closer, including inner product.** pgvector's `<#>` operator returns the *negative* inner product for exactly that reason, so results always come back in ascending order.

| `DistanceFunction` | pgvector operator | Index operator class | What `Distance` holds                               |
| ------------------ | ----------------- | -------------------- | --------------------------------------------------- |
| `Cosine` (default) | `<=>`             | `vector_cosine_ops`  | `1 - cosine similarity`, from 0 (identical) to 2    |
| `L2`               | `<->`             | `vector_l2_ops`      | Euclidean distance, 0 or more                       |
| `InnerProduct`     | `<#>`             | `vector_ip_ops`      | The negative inner product; more negative is closer |

Use the metric your embedding model was trained for. For most text embedding models that is cosine. Inner product gives the same ranking as cosine for unit-length vectors and is cheaper to compute, but it only ranks meaningfully when the vectors are normalized.

## Store-neutral search {#store-neutral-search}

The extension methods above are the Marten-flavored entry point. The same two searches are also reachable through `IDocumentReadOperations.Search`, the store-agnostic session contract shared with Polecat and Fisher, so a library or a retrieval helper can run a vector search without referencing any store package:

<!-- snippet: sample_pgvector_neutral_search_accessor -->
<a id='snippet-sample_pgvector_neutral_search_accessor'></a>
```cs
// IDocumentReadOperations is JasperFx's store-agnostic session contract — no Marten type
// appears in this code, so the same method body runs against Polecat or Fisher.
IDocumentReadOperations operations = session;

var matches = await operations.Search.VectorSearchWithScoresAsync<Memo>(
    x => x.Embedding, Query, limit: 2);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PgVector.Tests/SingleTenancy/shared_search_surface.cs#L70-L77' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_pgvector_neutral_search_accessor' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`Search` is an **accessor** rather than a set of members on the session, and deliberately so: an instance method named `VectorSearchWithScoresAsync` would win overload resolution over Marten.PgVector's extension method of the same name at every existing call site, silently and with different behavior. Behind an accessor the collision cannot happen.

`UsePgVector()` is what supplies the implementation. A session from a store that never called it throws `NotSupportedException` from `Search` rather than returning an empty list.

### The `filter` predicate

Both signatures on `IDocumentSearchOperations` take an optional `Expression<Func<T, bool>>`:

```csharp
Task<IReadOnlyList<VectorMatch<T>>> VectorSearchWithScoresAsync<T>(
    Expression<Func<T, object?>> member,
    ReadOnlyMemory<float> query,
    int limit = 10,
    DistanceFunction? distance = null,
    Expression<Func<T, bool>>? filter = null,
    CancellationToken token = default) where T : notnull;
```

The predicate is parsed by the same `WhereClauseParser` Marten's LINQ provider uses and spliced into the search's own SQL, so it **supports and refuses what `Query<T>().Where(...)` does** — including predicates over child collections.

<!-- snippet: sample_pgvector_search_filter -->
<a id='snippet-sample_pgvector_search_filter'></a>
```cs
var matches = await ((IDocumentReadOperations)session).Search.VectorSearchWithScoresAsync<Memo>(
    x => x.Embedding, Query, limit: 1, filter: x => x.Category == "blue");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PgVector.Tests/SingleTenancy/shared_search_surface.cs#L114-L117' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_pgvector_search_filter' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

**The filter is applied before the limit**, not after, so the result is the top-k of the filtered set rather than the filtered remains of the top-k. In a hybrid search it reaches both halves, before each half's `CandidateDepth` — otherwise rows the caller is about to discard would consume the depth and the fused order would be a ranking of a set that includes them.

::: warning Recall under a selective filter
pgvector applies the `WHERE` **after** the HNSW index scan, and that scan is bounded by `hnsw.ef_search` (default 40). A selective filter can therefore return **fewer than `limit`** rows even though more matching documents exist — the filter discarded rows the index had already chosen. Raising `ef_search` on the connection, or dropping the index so the search is an exact scan, are the two ways to widen it. Stores that do an exact scan (Fisher) have no such bound; see [marten#5419](https://github.com/JasperFx/marten/issues/5419).
:::

### `distance: null` means the index's metric

`distance` on the neutral contract is `DistanceFunction?`, and **null means "the metric the `VectorIndex` for that member declared"**, falling back to `Cosine` when the member has no index. If a member has two indexes for two metrics, there is no single answer and the search throws rather than guessing — name the metric you want.

The `distance` parameter on the `VectorSearchAsync` / `VectorSearchWithScoresAsync` **extension methods** is unchanged and still defaults to `DistanceFunction.Cosine`, so no call written before 9.37 changes behavior.

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

- **`dimensions` must equal the query vector's length.** The search casts to `vector(N)` using the query vector's length, and that cast is part of the indexed expression. A query vector of a different length is refused by name, against the length the index declared:

  ```text
  The query vector has 2 dimensions, and 'ProductWithVector.Embedding' declares 1536. A vector
  search compares lengths, so this cannot be answered: embed the query with the same model the
  stored embeddings came from.
  ```

  A member with **no** declared index is not length-checked, because there is no declared length to check against — the searches work without an index, which is what makes them fast rather than what makes them possible.
- **The member must be the one you search.** The indexed expression is built from the same member path and serializer casing that `VectorSearchAsync` uses, so declaring it through `VectorIndex` keeps the two in step.

### Recall, and what Marten sets for you <Badge type="tip" text="9.37" />

HNSW is approximate: one index scan only ever considers `hnsw.ef_search` candidates, and pgvector defaults that to **40**. Marten sizes it per search, so a search asking for 100 rows gets 100:

- `hnsw.ef_search` is set to the number of rows the search needs — the `limit`, or a hybrid search's candidate depth — never below pgvector's default of 40 and **clamped to pgvector's ceiling of 1000**, which it enforces with an error rather than by rounding down.
- On pgvector 0.8 and later, `hnsw.iterative_scan` is set to `strict_order`. That is what makes a **filtered** search return its limit: pgvector applies a predicate *after* the index scan, so without it a selective filter — a conjoined tenant id, a soft-delete predicate, your own `filter` — thins the candidates and the search under-returns however large `ef_search` is.

Both are `SET LOCAL`, issued as statements in the **same batch** as the search itself. Postgres runs the statements of one batch in an implicit transaction, so the settings apply to the search and are discarded when it ends — they never leak onto a pooled connection, and the search needs no transaction of its own to scope them. When the session does own a transaction, they end with that transaction instead; neither setting affects anything but an HNSW index scan. Neither is set at all when the member has no vector index, because an exact scan already returns everything asked for.

::: tip
`strict_order` rather than `relaxed_order` is deliberate. The faster setting may return rows slightly out of distance order, and `VectorMatch<T>` promises nearest-first. See [pgvector's query options](https://github.com/pgvector/pgvector#query-options) for the trade.
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
- **The implementation is shared.** `ReciprocalRankFusion.Fuse` in `JasperFx.Events.Vectors` is one implementation for all three stores. It fuses **by key**, so the legs need not be the same document type — which is what lets you fuse a snapshot document carrying the tsvector with a separate embedding document, the shape a vector projection writes.
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
| `Distance`       | `null`                      | The metric for the vector search. **Null means the metric the member's `VectorIndex` declared**, and `Cosine` when there is none. See the warning below.               |
| `TextStyle`      | `HybridTextStyle.PlainText` | `PlainText` uses `plainto_tsquery` (every word, no syntax). `WebStyle` uses `websearch_to_tsquery` (quoted phrases, `or`, a leading `-` to exclude).                   |
| `RegConfig`      | `null`                      | The PostgreSQL text search configuration; null means `"english"`. It also selects which full text index the text search uses.                                          |

Both text styles are safe to feed raw search-box input. Raw `to_tsquery` syntax is deliberately not offered, because a malformed query would fail the whole fused search.

::: warning Behavior change in 9.37
`HybridSearchOptions` is now `JasperFx.Events.Vectors.HybridSearchOptions`, shared with Polecat and Fisher, and its `Distance` **defaults to `null` instead of `Cosine`**.

Marten's own copy defaulted to `Cosine` while the other two stores had no such default, so the same code over an index declared for `L2` produced a cosine ordering on Marten and an L2 ordering elsewhere — with nothing reported, and with the HNSW index silently unused, because pgvector matches an index to a query by its operator.

If your index is cosine, nothing moves. **If your index is `L2` or `InnerProduct` and you were relying on the old default, your hybrid searches now return a different order** — the one your index was built for. Pass `Distance: DistanceFunction.Cosine` explicitly to keep the old behavior.
:::

### The text leg

The text search runs `WHERE <tsvector> @@ <tsquery> ORDER BY ts_rank(<tsvector>, <tsquery>) DESC`. It picks the `tsvector` the same way Marten's LINQ [full text search](/documents/full-text) does, by looking for a full text index on the document registered with the same `RegConfig`:

- **One matching index.** The search uses that index's expression, so its GIN index serves the match. [Weighted indexes](/documents/full-text#weighted-full-text-indexes-and-relevance-ranking) are included, so a title match outranks a body match. GIN indexes cannot order, so `ts_rank` still sorts every matched row.
- **No matching index.** The search falls back to `to_tsvector('<RegConfig>'::regconfig, data)` over the *whole* JSON document, computed row by row. That is a sequential scan that matches every string value in the document. It works on small tables and gets expensive fast on large ones. An index registered with a different `regConfig` (for example `FullTextIndex("simple")`) is not used unless you pass that value as `RegConfig`.
- **Two or more matching indexes.** Marten can't tell which one you mean and throws `AmbiguousFullTextIndexException`. Give the indexes different `regConfig` values and pass the one you want, or register a single index covering every member you need.

## Event-sourced vector projection

`VectorProjection<TId>` is a base class for projections that maintain an embedding table alongside your streams. It maps events to text, hashes the text, calls your `IEmbeddingProvider` for anything new or changed, and writes the embeddings. When content hasn't changed, it skips the call to the model.

Since 9.37 the body of that — folding a page of events down to one text per document, hashing it, comparing against the stored hash, and batching the model call — is `VectorProjectionMap<TId>` and `VectorEmbeddingPlan<TId>` in `JasperFx.Events.Vectors`, shared with Polecat and Fisher. Marten supplies only the read of the current hashes and the write of the rows.

The non-generic `VectorProjection` is `VectorProjection<Guid>` with the pre-9.37 fluent `VectorProjectionMapping` API, kept so existing projections compile and behave the same.

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

`VectorProjectionSearchAsync<TId>` runs an ordered-by-distance query against the projection table and returns each row's id, distance, and original content text as a `VectorProjectionMatch<TId>`. It takes a `ReadOnlyMemory<float>`, which is what `IEmbeddingProvider` hands back:

```csharp
var matches = await session.VectorProjectionSearchAsync<string>(
    "note_vectors", queryVector, limit: 10);
```

The pre-9.37 overload taking a `Pgvector.Vector` and returning `VectorSearchResult` (with a `Guid` `Id` and a `float` `Distance`) is kept for existing call sites:

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

The table name is unqualified; Marten looks for it in the event store's schema. Results are rows, not documents, so load whatever the ids refer to yourself. `VectorIndex` applies to document tables only, and the projection table has no vector index, so these searches are sequential scans.

### Keying on something other than a `Guid`

`VectorProjection<TId>` is open over its identity, and the table's primary key is typed from `TId`, so a string-identified store can use it. Declare the map with `VectorProjectionMap<TId>`, whose content selector receives the `IEvent<T>` wrapper rather than the bare body — so stream identity, timestamp and headers are in reach as well as the payload:

<!-- snippet: sample_pgvector_generic_vector_projection -->
<a id='snippet-sample_pgvector_generic_vector_projection'></a>
```cs
/// <summary>
///     Keyed on the stream KEY rather than a Guid, which the pre-9.37 projection could not express at
///     all — its id was hardcoded to <see cref="Guid" /> (marten#5424).
/// </summary>
public class NoteVectorProjection(Neutral.IEmbeddingProvider provider)
    : VectorProjection<string>("note_vectors", provider)
{
    protected override void Configure(Neutral.VectorProjectionMap<string> map)
    {
        // The shared map's selector sees the IEvent<T> wrapper, so stream identity and metadata are
        // in reach as well as the body.
        map.Map<NoteWritten>(e => e.Data.Body, e => e.Data.NoteKey);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PgVector.Tests/SingleTenancy/vector_projection_shared_core.cs#L99-L116' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_pgvector_generic_vector_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

There is deliberately **no `Delete<TEvent>()` overload without an id selector**. A projection keyed on a member of the event that deletes by stream id addresses a row that was never written, so the delete matches nothing and the document stays in the index forever — silently, because a `DELETE` that hits no row is not an error. `map.Delete<T>(e => e.StreamId)` is the explicit spelling of the old default.

A content selector that throws is **not** caught. A swallowed exception returned `null`, which reads as "this event contributes no content", so a selector with a bug dropped the document out of the index with nothing reported anywhere. It now faults the shard, which is what the daemon's error handling is for.

### Building content from aggregate state

A selector that sees only one event cannot keep an embedding correct across a partial-update event. Given `NoteTitled { Title = "..." }`, returning the new title re-embeds the note *without* its body, and returning `null` leaves the embedding stale. `MapFromAggregate<TAggregate>` is the third answer — the text is built from the aggregate as it stands after the page's events:

<!-- snippet: sample_pgvector_map_from_aggregate -->
<a id='snippet-sample_pgvector_map_from_aggregate'></a>
```cs
public class Note
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";

    public void Apply(NoteWritten e) => Body = e.Body;
    public void Apply(NoteTitled e) => Title = e.Title;
}

/// <summary>
///     ⚠️ A content selector that sees only one event cannot keep an embedding correct across a
///     partial-update event: given <c>NoteTitled</c>, returning the new title re-embeds the note
///     without its body, and returning null leaves the embedding stale. Building the text from the
///     aggregate's CURRENT STATE is the third answer.
/// </summary>
public class NoteAggregateVectorProjection(Neutral.IEmbeddingProvider provider)
    : VectorProjection<string>("note_aggregate_vectors", provider)
{
    protected override void Configure(Neutral.VectorProjectionMap<string> map)
    {
        map.MapFromAggregate<Note>(
            note => $"{note.Title} {note.Body}".Trim(),
            (typeof(NoteWritten), e => e.StreamKey!),
            (typeof(NoteTitled), e => e.StreamKey!));
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PgVector.Tests/SingleTenancy/vector_projection_shared_core.cs#L118-L148' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_pgvector_map_from_aggregate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten builds the aggregate by **live aggregation** over the stream, which reads committed events. That makes `MapFromAggregate` an **async-lifecycle** feature: registered `Inline`, the aggregation would run before the page it is reacting to has been committed and would miss the very event that triggered it. It costs one stream read per affected document per page; content hashing does the rest, so a trigger that turns out not to change the built text costs no model call at all.

Live aggregation identifies a stream by `Guid` or by string, so `TId` has to be one of those when an aggregate mapping is declared.

### Conjoined tenancy <Badge type="tip" text="9.37" />

Register the table with the **options** rather than a schema name, and it is keyed `(tenant_id, id)`:

```csharp
opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable(opts));
```

That single change is the whole registration-side story. The projection then writes each event's
embedding, reads its content hashes, and deletes within **the tenant that event was appended for**, and
`VectorProjectionSearchAsync` filters on the session's tenant. That holds inline too, where one
`SaveChangesAsync` can carry several tenants' events: an append through
`session.ForTenant("tenant_b")` lands under `tenant_b`, not under the outer session's tenant
([#5439](https://github.com/JasperFx/marten/issues/5439)).

::: warning Upgrading an existing conjoined store
If the table already exists from an earlier version, switching to `BuildTable(opts)` migrates it: the
`tenant_id` column is added with the default `*DEFAULT*` and the table is re-keyed to `(tenant_id, id)`.
Every row already in it, whichever tenant it really came from, becomes a `*DEFAULT*` row: invisible to
every other tenant's search, and still returned to the default tenant's. **Truncate the table, then rebuild
the projection** so each tenant's embeddings are written under their own tenant. A rebuild alone leaves the
old `*DEFAULT*` rows beside the new ones. Content hashes are compared per tenant, so the rebuild calls the
embedding provider again for every row.
:::

::: warning
Before 9.37 the table had no `tenant_id` column at all, and none of these paths filtered. On a
conjoined store that meant one tenant's search returned another's rows and `content_text`; two
tenants owning a stream with the same id shared **one** row, so the later write replaced the
earlier tenant's embedding through `ON CONFLICT (id)`; and a delete in one tenant removed the
other's row. Nothing failed loudly. See [#5420](https://github.com/JasperFx/marten/issues/5420).

`BuildTable(schemaName)` still builds the single-tenant shape, which is correct for a single-tenant
store and what every existing registration is doing. On a **conjoined** store it is now refused when
the store is built, naming the overload to use — rather than leaking silently.
:::

Database-per-tenant was never affected and is unchanged: rows are written to the database the events
came from, and searches read from the session's database.

### Projection limitations

- **Async execution.** The synchronous `IProjection.Apply` overload throws; the projection only runs through its async path.

### Everything rides the session <Badge type="tip" text="9.37" />

The deletes and upserts are queued with `IDocumentOperations.QueueSqlCommand`, so they commit in the same transaction as the shard's progression. Before that the projection opened its own connection and executed them immediately, which meant an embedding survived a page the daemon rolled back — leaving the index describing events the store does not have.

The read of the current content hashes — the one thing that decides whether your embedding provider is called at all — goes through the same session. Marten's daemon session refuses `IQuerySession.Connection` outright, because "sticky" connections inside a projection are not supported, but that refusal is only of the connection: `IQuerySession.ExecuteReaderAsync` works, so the read has somewhere to go.

Inline, that means the hash read runs on the connection your writes are enlisted in. If you call `SaveChangesAsync` more than once inside a transaction you opened yourself (`SessionOptions.ForTransaction`), a later pass now sees the embeddings an earlier one wrote and skips them, where a read on a separate connection could not see them and re-embedded every one at your provider's meter. Under the async daemon nothing moves — that session opens a connection per read regardless — except that the read now carries the session's command timeout, resilience pipeline and `IMartenSessionLogger`.

The read never sees the page it is part of, and does not need to: a page is folded down to one write per id, in event order, before the hashes are read.

## Multi-tenancy

| API                                                | Conjoined tenancy (one database)               | Database per tenant    |
| -------------------------------------------------- | ---------------------------------------------- | ---------------------- |
| `VectorSearchAsync`, `VectorSearchWithScoresAsync` | Filtered to the session's tenant               | Isolated by connection |
| `HybridSearchAsync`, `HybridSearchWithScoresAsync` | Both searches filtered to the session's tenant | Isolated by connection |
| `VectorProjection`, `VectorProjectionSearchAsync`  | Tenant-scoped, via `BuildTable(opts)`          | Isolated by connection |

Document searches apply the same filters a `Query<T>()` would, taken from the document's own storage: a document type that is conjoined is filtered to the session's tenant, the default tenant included, and a type that is not multi-tenanted gets no tenant filter at all. Database-per-tenant setups are isolated at the connection level and need no extra filtering.

## Other Critter Stack stores

`IEmbeddingProvider`, `DistanceFunction`, `VectorMatch<T>`, `HybridSearchOptions`, `HybridMatch<T>`, `HybridTextStyle`, `ReciprocalRankFusion`, `IDocumentSearchOperations`, `VectorProjectionMap<TId>` and `VectorEmbeddingPlan<TId>` all live in `JasperFx.Events.Vectors` and are shared with the other Critter Stack document stores, so retrieval code, metric choices and projection declarations carry across. Each store documents its own details and limits:

- Polecat (SQL Server 2025): [full text search](https://polecat.jasperfx.net/documents/querying/full-text-search), [vector search](https://polecat.jasperfx.net/documents/querying/vector-search), [hybrid search](https://polecat.jasperfx.net/documents/querying/hybrid-search)
- Fisher (SQLite): [full text search](https://fisher.jasperfx.net/documents/querying/linq/full-text), [vector search](https://fisher.jasperfx.net/documents/querying/vector-search), [hybrid search](https://fisher.jasperfx.net/documents/querying/hybrid-search), [vector projections](https://fisher.jasperfx.net/events/projections/vector)

## What changed in 9.37 {#what-changed-in-9-37}

9.37 moves the rest of Marten.PgVector's search surface onto `JasperFx.Events.Vectors` ([marten#5429](https://github.com/JasperFx/marten/issues/5429)). Almost all of it is additive; three things are worth reading before you upgrade.

**1. `HybridSearchOptions.Distance` no longer defaults to `Cosine`.** It defaults to `null`, meaning the metric the member's `VectorIndex` declared. If your index is cosine nothing moves; if it is `L2` or `InnerProduct`, hybrid searches that passed no metric now return the order your index was built for instead of a cosine one. This is the reason the type is shared — the old default made the same code mean different things on Marten, Polecat and Fisher. The `VectorSearchAsync` extension methods are **not** changed.

**2. Vector and hybrid search now exclude soft-deleted documents.** They did not before; Polecat and Fisher did.

**3. `HybridSearchOptions`, `HybridMatch<T>` and `HybridTextStyle` moved namespace.** They were declared in `Marten.PgVector` in 9.36.0 and are now `JasperFx.Events.Vectors`' — the same three shapes, shared with Polecat and Fisher, which is what makes point 1 one default instead of three. Sharing the option record is not expressible as an overload, and `HybridMatch<T>` is a *return* type, so this is a source break for code written against 9.36.0. The fix is one line:

```csharp
using JasperFx.Events.Vectors;   // add this
```

Nothing else about the calls changes — the record's positional order is unchanged, `RegConfig` is still last, and every construction site compiles as written.

**4. A hand-written `IProjection` that also implements `IValidatedProjection<StoreOptions>` is now actually asked to validate.** `ProjectionGraph.AssertValidity` used to look at the `ProjectionWrapper` a bare `IProjection` is registered through, so the wrapped projection's own checks never ran ([jasperfx#845](https://github.com/JasperFx/jasperfx/issues/845)). Configuration errors that were silently passing can now fail when the store is built. That is the check doing its job, but it is a new failure at an old call site.

Additive in the same release:

| New                                       | What it is                                                                 |
| ----------------------------------------- | -------------------------------------------------------------------------- |
| [`session.Search`](#store-neutral-search) | Vector and hybrid search from the store-agnostic `IDocumentReadOperations` |
| [`filter`](#the-filter-predicate)         | A LINQ predicate on both searches, applied before the limit                |
| `VectorProjection<TId>`                   | A vector projection keyed on something other than a `Guid`                 |
| `MapFromAggregate<TAggregate>`            | Embedded text built from aggregate state rather than from one event        |
| `VectorProjectionSearchAsync<TId>`        | The generic form of the projection-table search                            |

The content hash stored beside each embedding is now spelled by `VectorEmbeddingPlan<TId>.HashOf` — lowercase hex SHA-256 of the UTF-8 text, where Marten wrote **uppercase** hex of the same bytes. The projection lowercases what it reads back, so **rows written by an earlier version still compare equal and nothing is re-embedded**. No migration is needed and no embedding provider is billed for the upgrade.

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

- The search methods run raw SQL, but they run it **through the session** <Badge type="tip" text="9.37" /> — its connection, its transaction, its command timeout, its resilience pipeline and its `IMartenSessionLogger`. A search on a session inside a caller-managed transaction sees that session's uncommitted writes, the way `Query<T>()` does. They still don't go through Marten's LINQ provider or compiled-query cache, documents are deserialized through the document's own storage rather than tracked by the session, and a `Where` clause is expressible through the [`filter`](#the-filter-predicate) on `session.Search`, which is parsed by Marten's own LINQ where-clause parser and spliced into the statement.
- Only simple member access expressions are supported in the vector property selector (`x => x.Embedding`), matching the Marten LINQ conventions.
- `VectorSearchAsync`, `VectorSearchWithScoresAsync` and `VectorProjectionSearchAsync` each have an **overload** taking a `CancellationToken` as the last argument <Badge type="tip" text="9.37" />, alongside the shipped token-less shape. It is an overload rather than a defaulted parameter on the existing method because an optional argument is compiled into the caller: widening a shipped signature would break every assembly that had not been rebuilt. The token-taking overload defaults nothing, which is also what keeps the two unambiguous — pass `limit` and `distance` explicitly when you pass a token.
