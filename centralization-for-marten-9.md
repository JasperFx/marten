# Centralization Plan for Marten 9: Shared Infrastructure with Polecat via Weasel.Core

## Context

Marten (PostgreSQL) and Polecat (SQL Server 2025) share significant infrastructure patterns. Common base types and interfaces should move to Weasel.Core so both projects can share them without database-specific coupling.

---

## Category 1: Metadata Interfaces — Identical, move to Weasel.Core

These are exact duplicates (modulo namespace and nullability) with zero database-specific logic. They're pure document metadata contracts.

| Interface | Marten (`Marten.Metadata`) | Polecat (`Polecat.Metadata`) | Status |
|-----------|--------|---------|--------|
| `IVersioned` | `Guid Version { get; set; }` | `Guid Version { get; set; }` | **Identical** |
| `ISoftDeleted` | `bool Deleted; DateTimeOffset? DeletedAt` | `bool Deleted; DateTimeOffset? DeletedAt` | **Identical** |
| `IRevisioned` | `int Version { get; set; }` | Does not exist yet | Marten-only; worth putting in Weasel.Core for Polecat to adopt later |
| `ITracked` | Non-nullable `string` members | Nullable `string?` members | **Needs alignment** — Polecat's nullable approach is more correct |

**Action:** Move all four to `Weasel.Core.Metadata` namespace. Align `ITracked` on nullable strings. Both Marten and Polecat type-forward or alias from their own namespaces.

---

## Category 2: Serialization Enums — Already partially shared

| Type | Weasel.Core | Marten | Polecat |
|------|-------------|--------|---------|
| `EnumStorage` | Yes (canonical) | Uses Weasel.Core's | Own copy (duplicate) |
| `Casing` | **No** | Defined in `ISerializer.cs` | Own copy (duplicate) |
| `CollectionStorage` | **No** | Defined in `ISerializer.cs` | Own copy (duplicate) |
| `NonPublicMembersStorage` | **No** | Defined in `ISerializer.cs` | Own copy (duplicate) |

**Action:** Move `Casing`, `CollectionStorage`, and `NonPublicMembersStorage` to Weasel.Core alongside the existing `EnumStorage`. Polecat already duplicates all three and can switch to the Weasel.Core versions.

---

## Category 3: ISerializer Interface — Strong overlap, worth unifying

### Shared surface (present in both Marten and Polecat)

```csharp
EnumStorage EnumStorage { get; }
Casing Casing { get; }
string ToJson(object document);
T FromJson<T>(Stream stream);
T FromJson<T>(DbDataReader reader, int index);
object FromJson(Type type, Stream stream);
object FromJson(Type type, DbDataReader reader, int index);
ValueTask<T> FromJsonAsync<T>(Stream stream, CancellationToken cancellationToken = default);
ValueTask<object> FromJsonAsync(Type type, Stream stream, CancellationToken cancellationToken = default);
```

### Marten-only additions

- `ValueCasting ValueCasting { get; }` — controls LINQ Select() casting behavior
- `string ToCleanJson(object? document)` — serialize without type metadata
- `string ToJsonWithTypes(object document)` — serialize with embedded type info
- `ValueTask<T> FromJsonAsync<T>(DbDataReader reader, int index, CancellationToken)` — async reader deserialization
- `ValueTask<object> FromJsonAsync(Type type, DbDataReader reader, int index, CancellationToken)` — async reader deserialization (non-generic)

### Polecat-only additions

- `T FromJson<T>(string json)` — string-based deserialization
- `object FromJson(Type type, string json)` — string-based deserialization (non-generic)

**Action:** Define a common `ISerializer` interface in Weasel.Core with the shared members. Both Marten and Polecat extend it with project-specific additions via their own derived interfaces.

---

## Category 4: IStorageOperation — Similar but divergent patterns

Both have a storage operation concept with `DocumentType`, `Role`, and `PostprocessAsync(DbDataReader)`, but the interfaces diverge:

| Aspect | Marten | Polecat |
|--------|--------|---------|
| Inheritance | Inherits `IQueryHandler` (LINQ) | Standalone |
| Role | `OperationRole Role()` (method) | `OperationRole Role { get; }` (property) |
| Postprocess | `IList<Exception>` parameter | No exceptions parameter |
| Command setup | Via `IQueryHandler` | `ConfigureCommand(ICommandBuilder)` |
| Document ID | Not on interface | `object? DocumentId` default method |
| Enum values | `Upsert, Insert, Update, Deletion, Patch, Events, Other` | `Upsert, Insert, Update, Delete, Patch` |

**Action:** Extract a minimal common `OperationRole` enum and a slim `IStorageOperation` base to Weasel.Core. Both projects extend with their specific needs. Lower priority since the divergence is larger.

---

## Category 5: Session Interfaces — Too divergent, do not share

`IQuerySession`, `IDocumentSession`, `IDocumentOperations`, `IDocumentStore` are conceptually similar but:

- **Marten** is much larger: `NpgsqlConnection Connection`, full-text search, bulk insert via PostgreSQL COPY, dirty tracking sessions, serializable isolation variants, 65+ members on `IDocumentStore`
- **Polecat** is intentionally minimal: ~10 members on `IDocumentStore`, no dirty tracking, no full-text search

Forcing a common interface would either bloat Polecat or gut Marten.

**Action:** Keep project-specific. No shared base.

---

## Recommended Priority Order

1. **Metadata interfaces** (`IVersioned`, `ISoftDeleted`, `ITracked`, `IRevisioned`) → Weasel.Core
   - Easiest win, zero risk, zero database coupling

2. **Serialization enums** (`Casing`, `CollectionStorage`, `NonPublicMembersStorage`) → Weasel.Core
   - Alongside existing `EnumStorage`. Eliminates Polecat duplicates

3. **ISerializer base interface** → Weasel.Core with shared members
   - Both projects extend for their extras

4. **OperationRole enum + minimal IStorageOperation** → Weasel.Core
   - Lower priority, interfaces diverge more

5. **Session interfaces** → Do not share
