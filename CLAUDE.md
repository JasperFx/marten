# Marten

.NET transactional document database and event store on PostgreSQL. Uses PostgreSQL's JSONB support for document storage with ACID guarantees and a full event sourcing implementation with projections.

**Repository:** https://github.com/JasperFx/marten
**Version:** 8.20.0 (`Directory.Build.props:4`)
**License:** MIT

## Tech Stack

- **Language:** C# 13.0, targets net8.0 / net9.0 / net10.0 (`Directory.Build.props:5,12`)
- **Database:** PostgreSQL 13+ (via Npgsql)
- **Key deps:** JasperFx, JasperFx.Events, JasperFx.RuntimeCompiler, Weasel.Postgresql, Newtonsoft.Json (`src/Marten/Marten.csproj:39-46`)
- **Build system:** Nuke (`build/build.cs`)
- **Test framework:** xUnit + Shouldly
- **Docs:** VitePress (`docs/`)

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
  Marten.CommandLine.Tests/- CLI tool tests
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
./build.sh test-cli               # Marten.CommandLine.Tests
./build.sh test-base-lib          # Marten.Testing
./build.sh test-code-gen          # Code generation round-trip
./build.sh test-extensions        # NodaTime + AspNetCore

# Docs
npm install && npm run docs
```

Default test connection: `Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres` (`build/build.cs:28`). Override with `marten_testing_database` env var.

## Test Harness

Base classes in `src/Marten.Testing/Harness/`:

- **`IntegrationContext`** (`IntegrationContext.cs:49`) - Standard integration tests. Shared `DocumentStore`, deletes all data between tests. Uses `[Collection("integration")]`.
- **`DestructiveIntegrationContext`** - Wipes entire public schema between tests.
- **`OneOffConfigurationsContext`** - Creates isolated `DocumentStore` with custom schema per test.
- **`BugIntegrationContext`** - Like `OneOffConfigurationsContext`, for bug regression tests.
- **`StoreFixture` / `StoreContext<T>`** - Share `DocumentStore` across tests via collection fixtures.
- **`SessionTypesAttribute`** (`IntegrationContext.cs:36`) - Theory data source for testing across None/IdentityOnly/DirtyTracking session modes.

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `marten_testing_database` | Override test connection string |
| `DEFAULT_SERIALIZER` | `SystemTextJson` or `Newtonsoft` (default) |
| `DISABLE_TEST_PARALLELIZATION` | `true` to disable parallel test execution |
| `postgresql_version` | Enforce specific PostgreSQL version detection |

## CI

GitHub Actions runs matrix builds across .NET 8/10, PostgreSQL 15/latest, and both serializers. Workflows in `.github/workflows/`. Tests run in Release config with parallelization disabled.

## Additional Documentation

When working on specific areas, check these files for patterns and conventions:

- [Architectural Patterns](.claude/docs/architectural_patterns.md) - Design patterns, DI conventions, session model, code generation, event sourcing patterns, and testing conventions used across the codebase.
- [VitePress Docs](docs/) - User-facing documentation with code samples referenced via `<!-- snippet: sample_* -->` markers tied to `#region sample_*` blocks in source.
- [CONTRIBUTING.md](CONTRIBUTING.md) - Contribution workflow, PR guidelines, git rebase strategy.
