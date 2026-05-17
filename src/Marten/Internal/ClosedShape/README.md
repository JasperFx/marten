# `Marten.Storage.Identification` — W3 spike

**Status: spike.** Not wired into production code. Validates the shape of
`IIdentification<TDoc, TId>` before W3 commits to the
~24-class closed-shape `DocumentStorage<TDoc, TId>` hand-write.

Tracking: [#4404](https://github.com/JasperFx/marten/issues/4404) work
stream W3.

## Why it exists

W3 of the zero-runtime-codegen plan hand-writes ~24
`DocumentStorage<TDoc, TId>` subclasses, one per
`(StorageStyle × Concurrency × Hierarchical)` tuple. Each of those
subclasses needs to **read** a document's id member and **assign** one
when missing — currently a codegen-emitted method on the runtime-generated
storage class, driven by `Marten.Schema.Identity.IIdGeneration`'s
`GenerateCode(GeneratedMethod, DocumentMapping)`.

Without a shared seam, every storage variant would have to know how to
do that for every identity strategy:

> 24 storage variants × ~6 identity strategies = ~144 implementations

The seam this spike sketches collapses the matrix:

> 24 storage variants + ~6 identity strategies = ~30 implementations

The storage subclasses compose with an `IIdentification<TDoc, TId>`
instance picked at boot time. Per-call cost is one virtual dispatch into
the strategy plus whatever the strategy's body does — a getter delegate
read (no-op path) or a sequence/CombGuid call (generate path). No runtime
branch on identity strategy after startup.

## What's in here

| File | Role |
| --- | --- |
| [`IIdentification.cs`](IIdentification.cs) | The contract — `Identity(TDoc)` + `AssignIfMissing(TDoc, IMartenDatabase)`. |
| [`SequentialGuidIdentification.cs`](SequentialGuidIdentification.cs) | `Guid` id, no DB round-trip — uses `CombGuidIdGeneration.NewGuid()`. Replaces `IIdGeneration` codegen for the `SequentialGuidIdGeneration` strategy. Accessor delegates from `LambdaBuilder` (FEC). |
| [`HiloIntIdentification.cs`](HiloIntIdentification.cs) | `int` id from `IMartenDatabase.Sequences.SequenceFor(documentType).NextInt()`. Replaces `IIdGeneration` codegen for the `HiloIdGeneration` `int` branch. Accessor delegates from `LambdaBuilder` (FEC). |
| [`HiloLongIdentification.cs`](HiloLongIdentification.cs) | `long` sibling of the above. |

What's not in here yet, but the design accommodates:

* **Strong-typed IDs / value types.** The accessor delegates take care of
  wrap/unwrap on read + write; an additional strategy class isn't needed
  per wrapped type.
* **`IdentityKeyGeneration`** (`"DocumentType/123"` string keys).
  Composes a Hilo-int call with a string-format step — small, follows
  the same pattern as the existing samples.
* **F# discriminated-union IDs.** Trickier accessor shape — handled by
  the source generator emitting case-aware get/set delegates rather than
  by additional strategy classes.
* **`NoOpIdGeneration` / externally-assigned keys.** A trivial impl
  whose `AssignIfMissing` is "return what the getter returned." Skipped
  here because there's nothing interesting to validate in the shape.

## Where the getter/setter delegates come from

Each strategy takes a `MemberInfo` (the document's id property or field)
at construction and builds its own getter + setter via JasperFx's
[`LambdaBuilder`](https://github.com/JasperFx/jasperfx/blob/master/src/JasperFx/Core/Reflection/LambdaBuilder.cs)
— FEC-compiled delegates, the same mechanism the existing
`DocumentStorage<T, TId>` already uses for its `_setter` field today
([`DocumentStorage.cs:98`](../../Internal/Storage/DocumentStorage.cs)).

```csharp
new SequentialGuidIdentification<Order>(
    typeof(Order).GetProperty(nameof(Order.Id))!);
```

The accessor is built once per `(TDoc, TId)` at startup and cached on
the strategy instance — per-call cost is one delegate invocation.

**Why FEC delegates rather than source-gen-emitted accessor classes:**
the strategic direction is to eliminate Marten's Roslyn-emitted runtime
codegen (today's `EventDocumentStorageGenerator` /
`GeneratedEventDocumentStorage` / etc.) — that's the heavyweight path
that builds an assembly per document type at boot. FEC-compiled lambdas
are the lightweight per-delegate alternative that stays in the codebase
once the Roslyn JIT path is gone. The W3 hand-write replaces the
Roslyn-emitted *storage classes*; `LambdaBuilder` keeps doing what it
already does for the *accessor delegates*.

## How it slots into the planned W3 storage class

```csharp
public abstract class DocumentStorage<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly IIdentification<TDoc, TId> _identification;

    protected DocumentStorage(IIdentification<TDoc, TId> identification, ...)
    {
        _identification = identification;
        ...
    }

    public TId Identity(TDoc document)
        => _identification.Identity(document);

    public TId AssignIdentity(TDoc document, string tenantId, IMartenDatabase database)
        => _identification.AssignIfMissing(document, database);

    // ... the (StorageStyle × Concurrency × Hierarchical) matrix lives
    //     in concrete subclasses; the identity surface is shared via
    //     the field above.
}
```

The 24 storage subclasses each just hold an `IIdentification<TDoc, TId>`
reference and call through.

## Open design questions

1. **Should `AssignIfMissing` take the tenant id?** Today
   `IDocumentStorage<T, TId>.AssignIdentity(T, string tenantId, IMartenDatabase)`
   takes it. None of the production identity strategies use it — but
   the seam is there. The spike drops the parameter; revisit if a real
   need surfaces.
2. **Sequence-key lookup.** `HiloInt/Long` strategies hold a `Type`
   reference and look up the sequence via
   `IMartenDatabase.Sequences.SequenceFor(type)`. The lookup is per-call
   today (`_database.Sequences` is a dict). Worth memoizing the
   `ISequence` at strategy-construction time once the actual storage
   classes wire this in — but the strategy lifetime needs to be
   per-`IMartenDatabase` for that to be correct, and the W3 spec has the
   storage class as per-`StoreOptions` (not per-database). Punt to
   storage-class wiring decision.
3. **Hierarchical document mapping.** Subclassed documents share an id
   member with their root. The identification strategy stays at the
   root-type level; subclass storage just composes with the same
   strategy instance. Confirm this still holds once
   `SubClassDocumentStorage<TParent, TChild>` is closed-shape.

## Tests

[`src/CoreTests/Storage/Identification/`](../../../CoreTests/Storage/Identification/)
exercises each strategy against an in-memory fake `IMartenDatabase` so
the shape is testable without a Postgres roundtrip.

## Companion spike: end-to-end closed-shape DocumentStorage

[`ClosedShape/`](ClosedShape/) takes the next step: a hand-written
`LightweightSequentialGuidStorage<TDoc>` that extends
`LightweightDocumentStorage<TDoc, Guid>`, composes with
`SequentialGuidIdentification<TDoc>`, and proves the closed-shape
pattern drives Marten's basic document-DB features end-to-end without
any runtime Roslyn codegen for the registered document type.

Files:

| File | Role |
| --- | --- |
| [`ClosedShape/LightweightSequentialGuidStorage.cs`](ClosedShape/LightweightSequentialGuidStorage.cs) | The storage class. One cell of the planned W3 matrix (Lightweight × Guid × no concurrency × no metadata). Inherits Store/Eject/LoadAsync/LoadManyAsync from `LightweightDocumentStorage`. |
| [`ClosedShape/ClosedShapeUpsertOperation.cs`](ClosedShape/ClosedShapeUpsertOperation.cs) | Hand-written `IDocumentStorageOperation`. Emits raw `INSERT … ON CONFLICT (id) DO UPDATE SET data = excluded.data` — bypasses the per-document `mt_upsert_*` PostgreSQL function entirely. |
| [`ClosedShape/ClosedShapeLightweightSelector.cs`](ClosedShape/ClosedShapeLightweightSelector.cs) | Hand-written `ISelector<T>`. Reads the data column at index 1 (the `DocumentTable.SelectColumns(Lightweight)` order is `id, data`). |
| [`ClosedShape/ClosedShapeRegistration.cs`](ClosedShape/ClosedShapeRegistration.cs) | `theStore.UseLightweightSequentialGuidClosedShape<TDoc>()` — registers the hand-written storage with the live `ProviderGraph` before any session runs. Bypasses the codegen-emit branch for the target document type. |

Integration tests exercise the full pipeline against a real Postgres:

* `store_save_load_round_trip` — Store → SaveChanges → LoadAsync.
* `store_assigns_a_sequential_guid_when_id_is_empty` — `Guid.Empty` flows through `IIdentification.AssignIfMissing` and is written back onto the document.
* `linq_query_returns_documents_persisted_via_closed_shape_storage` — Store 3, query by `Where(x => x.Name == ...)`.
* `delete_via_session_removes_the_row` — `Delete<TDoc>(id)` + SaveChanges.
* `upsert_overwrites_on_second_store_of_same_id` — second Store with same id replaces the row.

What this validates:

* The closed-shape storage class plugs into Marten's existing session pipeline (`SaveChangesAsync`, `LoadAsync`, LINQ provider, `Delete<TDoc>(id)`) via the existing `IDocumentStorage<T, TId>` contract.
* `IIdentification<TDoc, TId>` as a composition seam works in practice — identity reads + assignments flow cleanly through the storage class.
* No Roslyn JIT is invoked for the registered document type at boot. The full write + read + query path uses hand-written code.

Out of spike scope (mechanical to add):

* Metadata columns (`mt_version`, `mt_dotnet_type`, `mt_last_modified`, soft-delete columns, tenancy column). Skipped via `Policies.DisableInformationalFields()` in the tests; production W3 needs them.
* Optimistic concurrency + revisions (`Overwrite` throws).
* The other 23 storage-matrix cells (IdentityMap / DirtyTracking / QueryOnly × hierarchical × concurrency × revisions).
* Bulk insert (`IBulkLoader<T>` is stubbed at registration time).
* Tenancy.
