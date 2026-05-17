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
| [`SequentialGuidIdentification.cs`](SequentialGuidIdentification.cs) | `Guid` id, no DB round-trip — uses `CombGuidIdGeneration.NewGuid()`. Replaces `IIdGeneration` codegen for the `SequentialGuidIdGeneration` strategy. |
| [`HiloIntIdentification.cs`](HiloIntIdentification.cs) | `int` id from `IMartenDatabase.Sequences.SequenceFor(documentType).NextInt()`. Replaces `IIdGeneration` codegen for the `HiloIdGeneration` `int` branch. |
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

The strategy implementations take `Func<TDoc, TId>` getter +
`Action<TDoc, TId>` setter at construction. In the source-gen-output
world (W5), those come from a generator-emitted
`IDocumentAccessor<TDoc, TId>` type:

```csharp
// What the source generator will emit (sketch)
public sealed class OrderDocumentAccessor : IDocumentAccessor<Order, Guid>
{
    public Guid Read(Order doc) => doc.Id;
    public void Write(Order doc, Guid id) => doc.Id = id;
}
```

The storage subclass picks the accessor at construction:

```csharp
new SequentialGuidIdentification<Order>(
    accessor.Read,
    accessor.Write);
```

In the spike's tests, callers supply hand-written lambdas instead — the
strategy classes don't care which.

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
