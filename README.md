# Marten

## .NET Transactional Document DB and Event Store on PostgreSQL

[![Discord](https://img.shields.io/discord/1074998995086225460?color=blue&label=Chat%20on%20Discord)](https://discord.gg/WMxrvegf8H)
![Twitter Follow](https://img.shields.io/twitter/follow/marten_lib?logo=Twitter&style=flat-square)
[![Tests](https://github.com/JasperFx/marten/actions/workflows/tests.yml/badge.svg?branch=master)](https://github.com/JasperFx/marten/actions/workflows/tests.yml)
[![Nuget Package](https://badgen.net/nuget/v/marten)](https://www.nuget.org/packages/Marten/)
[![Nuget](https://img.shields.io/nuget/dt/marten)](https://www.nuget.org/packages/Marten/)

<div align="center">
    <img src="https://github.com/user-attachments/assets/f052d5a7-1f49-4aa7-91f6-cba415988d14" alt="marten logo" width="70%">
</div>

The Marten library provides .NET developers with the ability to use the proven [PostgreSQL database engine](http://www.postgresql.org/) and its [fantastic JSON support](https://web.archive.org/web/20230127180328/https://www.compose.com/articles/is-postgresql-your-next-json-database/) as a fully fledged [document database](https://en.wikipedia.org/wiki/Document-oriented_database). The Marten team believes that a document database has far reaching benefits for developer productivity over relational databases with or without an ORM tool.

Marten also provides .NET developers with an ACID-compliant event store with user-defined projections against event streams.

Access docs [here](https://martendb.io/). For any of your queries including the whole of Critter stack, join our [Discord channel](https://discord.gg/WMxrvegf8H) and it is the best way to reach us quickly. You can also raise questions/queries via [GitHub Discussions](https://github.com/JasperFx/marten/discussions) as well.

## Support Plans

<div align="center">
    <img src="https://www.jasperfx.net/logo.png" alt="JasperFx logo" width="70%">
</div>

While Marten is open source, [JasperFx Software offers paid support and consulting contracts](https://jasperfx.net/support-plans/) for Marten.

## Help us keep working on this project 💚

[Become a Sponsor on GitHub](https://github.com/sponsors/JasperFX) by sponsoring monthly or one time.

### Past Sponsors

<p align="left">
    <a href="https://aws.amazon.com/dotnet" target="_blank" rel="noopener noreferrer">
    <picture>
      <source srcset="https://martendb.io/dotnet-aws.png" media="(prefers-color-scheme: dark)" height="72px" alt=".NET on AWS" />
      <img src="https://martendb.io/dotnet-aws.png" height="72px" alt=".NET on AWS" />
    </picture>
  </a>
</p>

## Working with the Code

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) 9.0 **and** 10.0 — the libraries and test projects
  multi-target `net9.0;net10.0`
- [Docker](https://www.docker.com/) (recommended) or your own PostgreSQL 13+ server
- [Node.js](https://nodejs.org/en/) 22+ — only needed to work on the documentation website

The build is driven by [Nuke](https://nuke.build/) (`build/build.cs`). `build.sh`, `build.ps1` and
`build.cmd` are thin wrappers, so any target below can be run as `./build.sh <target>` on Linux/macOS,
`.\build.ps1 <target>` in PowerShell, or `build.cmd <target>` on Windows.

```bash
./build.sh compile     # restore and build src/Marten.slnx (the default target)
```

### PostgreSQL

Marten supports **PostgreSQL 13 or later**; the async daemon relies on `pg_current_snapshot()`,
which first shipped in 13.

The quickest way to get a test database is the Docker Compose file at the repository root:

```bash
docker compose up -d          # or: ./build.sh init-db  (starts it and waits until it is ready)
```

`docker-compose.yml` builds its image from `docker/postgres/Dockerfile`, which layers **PostGIS 3** and
**pgvector** onto the official `postgres:17` image, so the Marten.PostGIS and Marten.PgVector test
projects have the extensions they need. Both packages are multi-arch, so this builds natively on
Apple-silicon machines. It listens on port 5432 with the `postgres`/`postgres` login and a
`marten_testing` database. `./build.sh rebuild-db` tears the container down and starts it again.

A few suites need something other than the standard database:

| Suite                 | Database                                                                                                   |
|-----------------------|------------------------------------------------------------------------------------------------------------|
| Marten.TimescaleDB    | `docker compose -f docker-compose.timescaledb.yml up -d` (TimescaleDB on port 5433 — point `marten_testing_database` at it) |
| MultiHostTests        | `docker compose -f src/MultiHostTests/docker-compose.yaml up -d` (primary/standby pair on ports 5440/5441) |

PLV8 is no longer part of developing Marten — the patching API it once backed has been replaced by
[native partial updates](https://martendb.io/documents/partial-updates-patching.html), and neither the
compose image nor CI installs the extension. Older applications that still depend on it can use the
separate `Marten.PLv8` package.

### Test configuration

By default the tests connect to:

```text
Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres
```

To use a different server or PostgreSQL version, set environment variables rather than editing the
compose file:

| Variable                       | Purpose                                                                        |
|--------------------------------|--------------------------------------------------------------------------------|
| `marten_testing_database`      | Connection string for the test database (the login needs the `postgres` role) |
| `DEFAULT_SERIALIZER`           | `Newtonsoft` (default) or `SystemTextJson`                                     |
| `DISABLE_TEST_PARALLELIZATION` | `true` to run test collections serially, as CI does                           |

### Running the tests

The test suites use [xUnit.net v3](https://xunit.net/) and [Shouldly](https://github.com/shouldly/shouldly),
and are split across many test projects under `src/`. There is one build target per test project:

| Target                                         | Project                                       |
|------------------------------------------------|-----------------------------------------------|
| `test-base-lib`                                | `Marten.Testing` (shared harness)             |
| `test-core`                                    | `CoreTests` — schema management, retries, core services |
| `test-document-db`                             | `DocumentDbTests` — document storage features |
| `test-event-sourcing`                          | `EventSourcingTests` — events and projections |
| `test-daemon`                                  | `DaemonTests` — async projection daemon       |
| `test-linq`                                    | `LinqTests` — LINQ-to-SQL translation         |
| `test-patching`                                | `PatchingTests` — partial document updates    |
| `test-multi-tenancy`                           | `MultiTenancyTests`                           |
| `test-tenant-partitioned-events`               | `TenantPartitionedEventsTests`                |
| `test-value-types`                             | `ValueTypeTests` — strong-typed identifiers   |
| `test-modular-config`                          | `ModularConfigTests`                          |
| `test-container-scoped-projections`            | `ContainerScopedProjectionTests`              |
| `test-compiled-queries`                        | `CompiledQueryTests`                          |
| `test-source-generator`                        | `Marten.SourceGenerator.Tests` (no database)  |
| `test-aot-runtime`                             | Native AOT smoke test                         |
| `test-stress`                                  | `StressTests` (not run in CI)                 |
| `test-multi-host`                              | `MultiHostTests` (needs its own compose file) |
| `test-noda-time`, `test-aspnetcore`, `test-postgis`, `test-pgvector`, `test-timescaledb`, `test-entity-framework-core`, `test-memory-pack` | The extension packages |

Aggregate targets:

```bash
./build.sh test              # every core suite against the standard database
./build.sh test-extensions   # NodaTime, AspNetCore, PostGIS, PgVector, TimescaleDB, EF Core, MemoryPack
```

Useful options for any test target:

```bash
./build.sh test-event-sourcing --framework net10.0       # one TFM instead of every built TFM
./build.sh test-core --configuration Release
./build.sh test-core --disable-test-retry                # see a suite's real stability
```

Test targets don't shell out to `dotnet test`. They run each project through the
[Bobcat](https://github.com/JasperFx/bobcat) test supervisor (`build/SupervisedTests.cs`): a failing
test is retried in a **fresh process**, and a test that only passes on a retry is reported as
**flaky** — in the console, in the GitHub job summary and in a JSON ledger under
`artifacts/test-ledger/` — rather than being counted as a clean pass. For a genuinely racy test,
`[Trait("Retry", "3")]` raises its attempt limit and `[Trait("Isolated", "true")]` runs it in its own
process.

While you're iterating, you can also run a project or a single test directly from your IDE's test
runner or with the dotnet CLI:

```bash
dotnet test src/DocumentDbTests/DocumentDbTests.csproj --framework net10.0
```

Every test project must import `src/Tests.props`, which sets up xUnit v3 and the Microsoft Testing
Platform host the supervisor relies on.

#### Integration test harness

`src/Marten.Testing/Harness` has base classes that make integration tests against PostgreSQL efficient
and parallel-friendly:

- `IntegrationContext` — a shared, default-configured `DocumentStore`. Data is **not** cleared
  between tests, so if your assertions depend on a document type only holding what your test wrote,
  override `ClearedBeforeEachTest` to list those types.
- `DestructiveIntegrationContext` — like `IntegrationContext`, but wipes the `public` schema between
  tests. Use it sparingly.
- `OneOffConfigurationsContext` — for tests that configure their own store through
  `StoreOptions(...)`. Each fixture gets an isolated schema named after the test class.
- `BugIntegrationContext` — a `OneOffConfigurationsContext` that puts every bug-reproduction test in
  the shared `bugs` schema.
- `StoreFixture` / `StoreContext<T>` — share one custom-configured `DocumentStore` across a class
  through xUnit's `IClassFixture<T>`.

### Continuous integration

CI runs on GitHub Actions only ([`.github/workflows/tests.yml`](.github/workflows/tests.yml)). Each
test project is a separate job on its own runner and database, and the core suites run against both
supported combinations:

| .NET | PostgreSQL           | Serializer       |
|:----:|:---------------------|:-----------------|
|  9   | `postgres:15-alpine` | Newtonsoft       |
|  10  | `postgres:latest`    | System.Text.Json |

The extension suites use purpose-built images — `postgis/postgis:17-3.5`, `pgvector/pgvector:pg17` and
`timescale/timescaledb-ha:pg17` — and there are extra jobs for the Native AOT smoke test and for the
two-node replication pair used by `MultiHostTests`. A final `flakiness` job rolls up every job's retry
ledger. Tests run in Release with parallelization disabled.

**Adding a test project takes two changes:** a target in `build/build.cs` *and* a matrix entry in
`tests.yml`. A project with no CI entry isn't being run.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the contribution workflow and PR guidelines.

### Other build targets

| Target                   | Description                                               |
|--------------------------|-----------------------------------------------------------|
| `compile`                | Restore and build the solution (default)                  |
| `init-db` / `rebuild-db` | Start (or restart) the Docker Compose PostgreSQL database |
| `docs`                   | Run the documentation website locally (see below)         |
| `docs-build`             | Build the static documentation site                       |
| `clear-inline-samples`   | Strip generated snippet bodies out of the docs Markdown   |
| `benchmarks`             | Run the BenchmarkDotNet suite in `MartenBenchmarks`       |
| `pack`                   | Build the NuGet packages                                  |

## Documentation

The documentation at [martendb.io](https://martendb.io/) is written in Markdown under [`/docs`](docs),
built with [VitePress](https://vitepress.dev/), and hosted on Netlify. Code samples are pulled into the
Markdown from compiling, tested source code by [MarkdownSnippets](https://github.com/SimonCropp/MarkdownSnippets),
and search is provided by [Algolia DocSearch](https://docsearch.algolia.com/).

### Running the docs locally

```bash
dotnet tool restore   # installs the pinned mdsnippets tool from .config/dotnet-tools.json
npm install
npm run docs
```

`npm run docs` first runs `mdsnippets` to refresh every code snippet in the Markdown, then starts the
VitePress dev server at <http://localhost:5050> with hot reload. `./build.sh docs` does the same thing
and installs the npm packages and the `mdsnippets` tool for you.

Other scripts:

| Command              | Description                                          |
|----------------------|------------------------------------------------------|
| `npm run mdsnippets` | Refresh the code snippets in the Markdown only       |
| `npm run docs-build` | Refresh snippets, then build the static site         |
| `npm run vitepress-dev` | Start VitePress without refreshing snippets       |

To add a new page, create the Markdown file under `docs/` and add it to the sidebar in
[`docs/.vitepress/config.mts`](docs/.vitepress/config.mts).

### Code samples with MarkdownSnippets

Don't paste C# into the Markdown. Instead, mark the code in the source — usually in a test, so the
sample is compiled and exercised by the build — with a named region whose name starts with `sample_`:

```csharp
#region sample_my_snippet
var user = new User { FirstName = "Han" };
session.Store(user);
await session.SaveChangesAsync();
#endregion
```

Then reference it from a docs page with an empty snippet block:

```markdown
<!-- snippet: sample_my_snippet -->
<!-- endSnippet -->
```

When `mdsnippets` runs (as part of `npm run docs`), it fills the block in place with the code and a
link back to the source file on GitHub (see [`mdsnippets.json`](mdsnippets.json)). Search the
repository for `sample_` or `snippet:` to find plenty of examples.

A few rules:

- **Edit the source, not the Markdown.** The generated code between the snippet markers is overwritten
  on every run, so change the `#region` in the C# file.
- **Commit the regenerated Markdown** with your change, so reviewers can see the actual docs content
  in the PR.
- **Snippet names are unique across the repository** and a missing snippet fails the run
  (`TreatMissingAsWarning` is `false`).
- Some directories, including the core `src/Marten` library, are excluded as snippet sources in
  `mdsnippets.json` — put samples in test or sample projects.

### Linting

Pull requests that touch `docs/` run markdownlint and cspell
([`.github/workflows/docs-prs.yml`](.github/workflows/docs-prs.yml)). Run them locally first:

```bash
npx --yes markdownlint-cli@latest --disable MD009 -- "docs/**/*.md"
npx --yes cspell --config ./docs/cSpell.json "docs/**/*.md"
```

Add legitimate technical terms to the `words` list in `docs/cSpell.json`.

### Publishing

The docs are deployed to Netlify by the manually triggered
[Docs build and deploy](.github/workflows/docs.yml) workflow (`./build.sh publish-docs`, or
`publish-docs-preview` for a preview deploy).

> The Marten 3.x documentation lived in the `/documentation` folder and used a different tool; it is
> maintained only on the [3.14 branch](https://github.com/JasperFx/marten/tree/3.14).

## License

Copyright © Jeremy D. Miller, Babu Annamalai, Oskar Dudycz, Joona-Pekka Kokko and contributors.

Marten is provided as-is under the MIT license. For more information see [LICENSE](LICENSE).

## Code of Conduct

This project has adopted the code of conduct defined by the [Contributor Covenant](http://contributor-covenant.org/) to clarify expected behavior in our community.
