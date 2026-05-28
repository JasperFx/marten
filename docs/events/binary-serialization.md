# Binary Event Serialization <Badge type="tip" text="9.3" />

Marten can serialize individual event types to a binary wire format
([MemoryPack](https://github.com/Cysharp/MemoryPack),
[MessagePack](https://msgpack.org/), or anything else implementing
`IEventBinarySerializer`) instead of the default JSON, trading a few of JSON's
ergonomic wins for a meaningful throughput and storage-size improvement on
hot streams. See [#4515](https://github.com/JasperFx/marten/issues/4515) for
the design discussion.

The opt-in is **per event type** — binary-serialized and JSON-serialized events
coexist in the same `mt_events` table, so the feature can be rolled out on an
existing store with **no migration of existing data**.

## How it works

A second column, `bdata bytea NULL`, sits alongside the existing `data jsonb NOT NULL`
on `mt_events`. The row-level discriminator is `bdata IS NULL`:

| When | `data` | `bdata` |
| --- | --- | --- |
| Event uses the JSON serializer | full JSON payload | `NULL` |
| Event uses an `IEventBinarySerializer` | the placeholder `'{}'::jsonb` | the serialized bytes |

On read, Marten inspects `bdata`:

- `NULL` → existing JSON deserialization path. Pre-feature rows continue to work without conversion.
- non-null → `IEventBinarySerializer.Deserialize(eventType, bytes)`.

Because the discriminator is on the row and the serializer is resolved per
event type, the same stream can carry rows of either format with no special
handling at the call site.

## Quick start with `Marten.MemoryPack`

The companion `Marten.MemoryPack` NuGet package ships a ready-to-use
`IEventBinarySerializer` over MemoryPack:

```shell
dotnet add package Marten.MemoryPack
```

Mark event types you want to serialize as binary with both
`[BinaryEvent]` (so Marten picks them up) and `[MemoryPackable]` (so MemoryPack
can serialize them):

```csharp
using Marten.Events;
using MemoryPack;

[BinaryEvent]
[MemoryPackable]
public partial record TripStarted(Guid TripId, string DriverName, DateTimeOffset StartedAt);
```

Wire MemoryPack as the store-wide fallback for `[BinaryEvent]` types:

```csharp
using Marten.MemoryPack;

var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);

    // Wire MemoryPack as DefaultBinarySerializer. [BinaryEvent]-marked
    // event types resolve to this serializer on registration. Works with
    // every EventAppendMode (Rich / Quick / QuickWithServerTimestamps)
    // and with BulkEventAppender — see the "Append modes" section.
    opts.Events.UseMemoryPackSerializer();
});
```

Now `TripStarted` writes through MemoryPack to `bdata`; un-marked events
continue to write JSON to `data`.

## Registration ergonomics

Two equivalent ways to opt an event type in:

```csharp
// 1. Attribute-driven — uses opts.Events.DefaultBinarySerializer as the resolver.
[BinaryEvent]
[MemoryPackable]
public partial record TripEnded(Guid TripId, DateTimeOffset EndedAt);

// 2. Fluent — wire an explicit per-type serializer (overrides any default).
opts.Events.UseBinarySerializer<TripEnded>(new MemoryPackEventSerializer());
```

Resolution order on `EventMapping` construction:

1. Explicit `opts.Events.UseBinarySerializer<TEvent>(...)` for that type.
2. `[BinaryEvent]` attribute + `opts.Events.DefaultBinarySerializer`.
3. Otherwise, plain JSON (existing path).

If a type carries `[BinaryEvent]` but no per-type serializer was wired AND
`DefaultBinarySerializer` is `null`, Marten throws at the first append with a
remediation message naming both registration entry points.

## Bring your own serializer

`IEventBinarySerializer` is small enough to implement directly against any
binary format — MessagePack, protobuf, etc.:

```csharp
public interface IEventBinarySerializer
{
    byte[] Serialize(Type type, object data);
    object Deserialize(Type type, byte[] data);
}
```

The serializer is a singleton — keep its state thread-safe.

## On-disk shape

For binary events, `data` holds the literal `{}` placeholder so the existing
`data jsonb NOT NULL` constraint stays intact (no schema relaxation):

```sql
-- binary-serialized event
select type, data::text, bdata is null
from mt_events where seq_id = 42;
--    type        | data | bdata is null
-- --------------|------|---------------
--  trip_started | {}   | false

-- JSON-serialized event in the same stream
select type, data::text, bdata is null
from mt_events where seq_id = 43;
--    type                | data                            | bdata is null
-- --------------------- |---------------------------------|---------------
--  trip_comment_added   | {"comment": "looking good", …}  | true
```

## Migration

Purely additive: the only schema change is `bdata bytea NULL` on `mt_events`.
Existing rows have `bdata = NULL` (the column's default for prior data) and
read through the JSON path. Marten's standard schema migration creates the
column for existing installations — no event data conversion required.

## Append modes

Binary event serialization works with **every** `EventAppendMode` Marten
ships — `Rich`, `Quick`, and `QuickWithServerTimestamps`. The Quick modes
route appends through the `mt_quick_append_events` PostgreSQL function,
which carries a `bdatas bytea[]` parameter that's inserted into `mt_events.bdata`
in parallel with the existing `bodies jsonb[]`. `BulkEventAppender` (the
COPY-based bulk loader) also supports binary events — its COPY column list
includes `bdata`, and each event row writes either the binary payload or
NULL.

You don't have to think about the append mode: binary opt-in is per event
type and works identically across all of them.

## Schema evolution — use versioned event types

Marten's existing [event upcasters](/events/versioning) operate on the JSON
wire form and don't generalize to a `byte[]` payload, so they don't apply to
binary events. The recommended pattern for evolving a binary event's shape
is **introduce a new event type for each version** rather than upcasting in
place:

```csharp
// Original
[BinaryEvent]
[MemoryPackable]
public partial record TripStarted(Guid TripId, string DriverName);

// Schema change — new fields. Don't edit TripStarted; add a new type.
[BinaryEvent]
[MemoryPackable]
public partial record TripStartedV2(Guid TripId, string DriverName, DateTimeOffset StartedAt);
```

When the projection / aggregate handles both versions explicitly, old
streams keep replaying through the old type and new appends use the new
type:

```csharp
public class Trip
{
    public Guid Id { get; set; }
    public string DriverName { get; set; } = "";
    public DateTimeOffset? StartedAt { get; set; }

    public void Apply(TripStarted e)   { Id = e.TripId; DriverName = e.DriverName; }
    public void Apply(TripStartedV2 e) { Id = e.TripId; DriverName = e.DriverName; StartedAt = e.StartedAt; }
}
```

The coexistence design lets old rows (written as `TripStarted`) and new rows
(written as `TripStartedV2`) live on the same stream without migration.

### Why not in-place backward-compatible schema changes?

You *can* lean on MemoryPack's
[backward-compatible field evolution](https://github.com/Cysharp/MemoryPack#version-tolerant-format)
(`[MemoryPackOrder]`, nullable fields, the `VersionTolerant` mode) for
additive-only changes to a single event type. That works as long as the
serializer itself can deserialize old payloads into the new shape — but the
moment a change goes beyond the serializer's tolerance rules (renaming, type
changes, splitting a field), there's no JSON-style upcaster path to fall
back on. Versioning the event type works for every shape of change and stays
explicit about which version each row was written with.

### Mixing binary + JSON

If you have an existing JSON-serialized event and want a future version to
go binary, the same pattern applies: define a new `[BinaryEvent]`-marked
type for the new version, leave the old (JSON) type and its upcasters
alone, and have the aggregate handle both. The per-row dispatch already
copes with mixed formats on the same stream.

## See also

- [Optimizing Event Store Performance and Scalability](/events/optimizing)
- [Event Versioning](/events/versioning) (JSON upcasters)
