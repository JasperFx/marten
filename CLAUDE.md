# Marten

.NET transactional document database and event store on PostgreSQL. Uses PostgreSQL's JSONB support for document storage with ACID guarantees and a full event sourcing implementation with projections.

**Repository:** https://github.com/JasperFx/marten
**Version:** 9.0.0-alpha.1 (`Directory.Build.props:4`) — `9.0` long-lived dev branch
**License:** MIT

## Tech Stack

- **Language:** C# 13.0, targets net9.0 / net10.0 (`Directory.Build.props:5,12`)
- **Database:** PostgreSQL 13+ (via Npgsql)
- **Key deps:** JasperFx, JasperFx.Events, JasperFx.RuntimeCompiler, Weasel.Postgresql, Newtonsoft.Json (`src/Marten/Marten.csproj:39-46`)
- **Build system:** Nuke (`build/build.cs`)
- **Test framework:** xUnit + Shouldly
- **Docs:** VitePress (`docs/`)

## Coding Conventions

- **Naming:** Use PascalCasing for all `public` or `internal` members (types, methods, properties, fields, events). Use camelCasing for all `protected` and `private` members. (Private fields keep the conventional leading underscore, e.g. `_workTracker`.)

## Project Structure

```
src/
  Marten/                  - Core library (document store, event store, LINQ, sessions)
  Marten.AspNetCore/       - ASP.NET Core integration (streaming endpoints)
  Marten.NodaTime/         - NodaTime date/time support
  Marten.Testing/          - Shared test harness: fixtures, base classes, helpers
  CoreTests/               - Schema management, retries, core services
  DocumentDbTests/         - Document storage features
  EventSourcingTests/      - Event sourcing, projections, daemon
  LinqTests/               - LINQ-to-SQL translation
  MultiTenancyTests/       - Multi-tenancy isolation
  PatchingTests/           - Partial document updates
  ValueTypeTests/          - Strongly-typed ID support
  DaemonTests/             - Async projection daemon
  CommandLineRunner/       - CLI for codegen and DB management
  TestingSupport/          - Shared test infrastructure
build/                     - Nuke build automation
docs/                      - VitePress documentation site
```

### Key Source Directories Within `src/Marten/`

| Directory | Purpose |
|-----------|---------|
| `Events/` | Event sourcing: appenders, projections, aggregation, daemon |
| `Internal/Sessions/` | Session implementations (lightweight, identity map, dirty tracking) |
| `Internal/Storage/` | Document storage variants and providers |
| `Internal/Operations/` | Storage operations (insert, update, upsert, delete) |
| `Internal/CodeGeneration/` | Runtime code generation for storage providers |
| `Linq/` | LINQ query parsing, SQL generation, compiled queries |
| `Schema/` | Document mapping, indexes, DDL generation |
| `Storage/` | Tenancy strategies, database management |
| `Patching/` | Fluent partial-update API |
| `Subscriptions/` | Event subscription processing |
| `Metadata/` | Metadata policies (versioning, soft delete, timestamps) |

## Build & Test

**Prerequisites:** .NET SDK 8.0+, PostgreSQL 13+ (Docker recommended)

```bash
# Start PostgreSQL
docker-compose up -d

# Build
./build.sh compile

# Run all tests
./build.sh test

# Run individual test suites
./build.sh test-core              # CoreTests
./build.sh test-document-db       # DocumentDbTests
./build.sh test-event-sourcing    # EventSourcingTests
./build.sh test-linq              # LinqTests
./build.sh test-multi-tenancy     # MultiTenancyTests
./build.sh test-patching          # PatchingTests
./build.sh test-value-types       # ValueTypeTests
./build.sh test-base-lib          # Marten.Testing
./build.sh test-code-gen          # Code generation round-trip
./build.sh test-extensions        # NodaTime + AspNetCore

# Docs
npm install && npm run docs
```

Default test connection: `Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres` (`build/build.cs:28`). Override with `marten_testing_database` env var.

## Test Harness

Base classes in `src/Marten.Testing/Harness/`:

- **`IntegrationContext`** (`IntegrationContext.cs:49`) - Standard integration tests. Shared `DocumentStore`, **data is NOT cleared between tests**. Uses `[Collection("integration")]`.
- **`DestructiveIntegrationContext`** - Wipes entire public schema between tests.
- **`OneOffConfigurationsContext`** - Creates isolated `DocumentStore` with custom schema per test.
- **`BugIntegrationContext`** - Like `OneOffConfigurationsContext`, but pins every bug test to one shared `bugs` schema.
- **`StoreFixture` / `StoreContext<T>`** - Share `DocumentStore` across tests via collection fixtures.
- **`SessionTypesAttribute`** (`IntegrationContext.cs:36`) - Theory data source for testing across None/IdentityOnly/DirtyTracking session modes.

### Clearing data between tests

**`IntegrationContext` does not clear the store between tests.** `DefaultStoreFixture` calls
`CompletelyRemoveAllAsync()` exactly once, when the fixture is created — never per test. The same is
true of `OneOffConfigurationsContext` within its collection.

That is deliberate: resetting everywhere would cost more than it is worth. The consequence is that a
test asserting on a *global* count or ordering — `Query<Target>().CountAsync()` with no filter, say —
only passes while it happens to run before its siblings, and any change to test ordering turns that
into a failure. Several tests had to be fixed for exactly this reason; see
[#5070](https://github.com/JasperFx/marten/issues/5070).

If your assertions depend on a document type holding only what your test wrote, say so explicitly.
On `IntegrationContext` and `StoreContext<T>`, declare the types — they are cleared before each test
in that class:

```csharp
protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(Target)];
```

`OneOffConfigurationsContext` and `BugIntegrationContext` build their store lazily from the test's own
`StoreOptions(...)` call, so there is nothing to clear that early. Those clear inline, after
configuring the store:

```csharp
await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));
```

Either way, name the types you actually depend on rather than reaching for `ResetAllData()`.

Note `BugIntegrationContext` pins **every** bug test across **every** suite to the single `bugs`
schema, and CI runs several test projects sequentially against one database, so a global count there
can pick up rows written by an entirely different project.

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `marten_testing_database` | Override test connection string |
| `DEFAULT_SERIALIZER` | `SystemTextJson` or `Newtonsoft` (default) |
| `DISABLE_TEST_PARALLELIZATION` | `true` to disable parallel test execution |
| `postgresql_version` | Enforce specific PostgreSQL version detection |

## CI

GitHub Actions runs matrix builds across .NET 8/10, PostgreSQL 15/latest, and both serializers. Workflows in `.github/workflows/`. Tests run in Release config with parallelization disabled.

## Documentation linting (run locally before pushing doc changes)

The `Documentation PRs` workflow (`.github/workflows/docs-prs.yml`) runs **markdownlint** and **cspell** over every file under `docs/`. Both are fast and cheap to run locally — do so whenever a commit touches `docs/**/*.md` (or a source file whose comment is pulled into a `<!-- snippet: ... -->` block) **before** pushing, so the PR doesn't have to wait on CI to surface lint failures:

```bash
# markdownlint — same args CI uses
npx --yes markdownlint-cli@latest --disable MD009 -- "docs/**/*.md"

# cspell — same args CI uses (requires Node >= 22; use nvm if your default is older)
npx --yes cspell --config ./docs/cSpell.json "docs/**/*.md"
```

Common issues to watch for:

- **MD051 (link-fragments).** Heading slugs are generated from the heading text after stripping markdown but **including** trailing inline components like `<Badge type="tip" text="9.0" />`, which leaves a trailing hyphen on the slug. If you intend to link to a heading section, either drop the `<Badge>` from the heading (use a `::: tip` callout under it instead) or write the link with the trailing-hyphen slug.
- **Snippet drift.** Snippet blocks (`<!-- snippet: sample_* -->`) are mechanically regenerated from `#region sample_*` markers in the source tree. Edit the source comment, not the doc markdown copy, or the next snippet refresh wipes the markdown change.
- **cspell unknown words.** Add legitimate technical terms to `docs/cSpell.json`'s `words` array rather than papering over with `<!-- cspell:ignore ... -->`.

## Additional Documentation

When working on specific areas, check these files for patterns and conventions:

- [Architectural Patterns](.claude/docs/architectural_patterns.md) - Design patterns, DI conventions, session model, code generation, event sourcing patterns, and testing conventions used across the codebase.
- [VitePress Docs](docs/) - User-facing documentation with code samples referenced via `<!-- snippet: sample_* -->` markers tied to `#region sample_*` blocks in source.
- [CONTRIBUTING.md](CONTRIBUTING.md) - Contribution workflow, PR guidelines, git rebase strategy.
