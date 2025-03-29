# Marten

## .NET Transactional Document DB and Event Store on PostgreSQL

[![Discord](https://img.shields.io/discord/1074998995086225460?color=blue&label=Chat%20on%20Discord)](https://discord.gg/WMxrvegf8H)
![Twitter Follow](https://img.shields.io/twitter/follow/marten_lib?logo=Twitter&style=flat-square)
[![Windows Build Status](https://ci.appveyor.com/api/projects/status/va5br63j7sbx74cm/branch/master?svg=true)](https://ci.appveyor.com/project/jasper-ci/marten/branch/master)
[![Linux Build status](https://dev.azure.com/jasperfx-marten/marten/_apis/build/status/marten?branchName=master)](https://dev.azure.com/jasperfx-marten/marten/_build/latest?definitionId=1&branchName=master)
[![Nuget Package](https://badgen.net/nuget/v/marten)](https://www.nuget.org/packages/Marten/)
[![Nuget](https://img.shields.io/nuget/dt/marten)](https://www.nuget.org/packages/Marten/)

<div align="center">
    <img src="https://github.com/user-attachments/assets/f052d5a7-1f49-4aa7-91f6-cba415988d14" alt="marten logo" width="70%">
</div>

The Marten library provides .NET developers with the ability to use the proven [PostgreSQL database engine](http://www.postgresql.org/) and its [fantastic JSON support](https://web.archive.org/web/20230127180328/https://www.compose.com/articles/is-postgresql-your-next-json-database/) as a fully fledged [document database](https://en.wikipedia.org/wiki/Document-oriented_database). The Marten team believes that a document database has far reaching benefits for developer productivity over relational databases with or without an ORM tool.

Marten also provides .NET developers with an ACID-compliant event store with user-defined projections against event streams.

Access docs [here](https://martendb.io/) and v3.x docs [here](https://martendb.io/v3).

## Support Plans

<div align="center">
    <img src="https://www.jasperfx.net/logo.png" alt="JasperFx logo" width="70%">
</div>

While Marten is open source, [JasperFx Software offers paid support and consulting contracts](https://bit.ly/3szhwT2) for Marten.

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

Before getting started you will need the following in your environment:

### 1. .NET SDK 8.0+

Available [here](https://dotnet.microsoft.com/download)

### 2. PostgreSQL 12 or above database

The fastest possible way to develop with Marten is to run PostgreSQL in a Docker container. Assuming that you have Docker running on your local box, type:
`docker-compose up`
or
`dotnet run --framework net6.0 -- init-db`
at the command line to spin up a Postgresql database withThe default Marten test configuration tries to find this database if no
PostgreSQL database connection string is explicitly configured following the steps below:

### Native Partial Updates/Patching

Marten supports native patching since v7.x. you can refer to [patching api](https://martendb.io/documents/partial-updates-patching.html) for more details.

### PLV8

If you'd like to use [PLV8 Patching Api](https://martendb.io/documents/plv8.html#the-patching-api) you need to enable the PLV8 extension inside of PostgreSQL for running JavaScript stored procedures for the nascent projection support.

Note that PLV8 patching will be deprecated in future versions and native patching is the drop in replacement for it. You can easily migrate to native patching, refer [here](https://martendb.io/documents/partial-updates-patching.html#patching-api) for more details.

Ensure the following:

- The login you are using to connect to your database is a member of the `postgres` role
- An environment variable of `marten_testing_database` is set to the connection string for the database you want to use as a testbed. (See the [Npgsql documentation](http://www.npgsql.org/doc/connection-string-parameters.html) for more information about PostgreSQL connection strings ).

_Help with PSQL/PLV8_

- On Windows, see [this link](http://www.postgresonline.com/journal/archives/360-PLV8-binaries-for-PostgreSQL-9.5-windows-both-32-bit-and-64-bit.html) for pre-built binaries of PLV8
- On *nix, check [marten-local-db](https://github.com/eouw0o83hf/marten-local-db) for a Docker based PostgreSQL instance including PLV8.

### Test Config Customization

Some of our tests are run against a particular PostgreSQL version. If you'd like to run different database versions, you can do it by setting `POSTGRES_IMAGE` env variables, for instance:

```bash
POSTGRES_IMAGE=postgres:15.3-alpine docker compose up
```

Tests explorer should be able to detect database version automatically, but if it's not able to do it, you can enforce it by setting `postgresql_version` to a specific one (e.g.)

```shell
postgresql_version=15.3
```

Once you have the codebase and the connection string file, run the [build command](https://github.com/JasperFx/marten#build-commands) or use the dotnet CLI to restore and build the solution.

You are now ready to contribute to Marten.

See more in [Contribution Guidelines](CONTRIBUTING.md).

### Tooling

* Unit Tests rely on [xUnit](http://xunit.github.io/) and [Shouldly](https://github.com/shouldly/shouldly)
* [Bullseye](https://github.com/adamralph/bullseye) is used for build automation.
* [Node.js](https://nodejs.org/en/) runs our Mocha specs.
* [Storyteller](http://storyteller.github.io) for some of the data intensive automated tests

### Build Commands

| Description                         | Windows Commandline      | PowerShell               | Linux Shell             | DotNet CLI                                                |
|-------------------------------------|--------------------------|--------------------------|-------------------------|-----------------------------------------------------------|
| Run restore, build and test         | `build.cmd`              | `build.ps1`              | `build.sh`              | `dotnet build src\Marten.sln`                             |
| Run all tests including mocha tests | `build.cmd test`         | `build.ps1 test`         | `build.sh test`         | `dotnet run --project build/build.csproj -- test`         |
| Run just mocha tests                | `build.cmd mocha`        | `build.ps1 mocha`        | `build.sh mocha`        | `dotnet run --project build/build.csproj -- mocha`        |
| Run StoryTeller tests               | `build.cmd storyteller`  | `build.ps1 storyteller`  | `build.sh storyteller`  | `dotnet run --project build/build.csproj -- storyteller`  |
| Open StoryTeller editor             | `build.cmd open_st`      | `build.ps1 open_st`      | `build.sh open_st`      | `dotnet run --project build/build.csproj -- open_st`      |
| Run docs website locally            | `build.cmd docs`         | `build.ps1 docs`         | `build.sh docs`         | `dotnet run --project build/build.csproj -- docs`         |
| Publish docs                        | `build.cmd publish-docs` | `build.ps1 publish-docs` | `build.sh publish-docs` | `dotnet run --project build/build.csproj -- publish-docs` |
| Run benchmarks                      | `build.cmd benchmarks`   | `build.ps1 benchmarks`   | `build.sh benchmarks`   | `dotnet run --project build/build.csproj -- benchmarks`   |

> Note: You should have a running Postgres instance while running unit tests or StoryTeller tests.

### xUnit.Net Specs

The tests for the main library are now broken into three testing projects:

1. `CoreTests` -- basic services like retries, schema management basics
1. `DocumentDbTests` -- anything specific to the document database features of Marten
1. `EventSourcingTests` -- anything specific to the event sourcing features of Marten

To aid in integration testing, Marten.Testing has a couple reusable base classes that can be use
to make integration testing through Postgresql be more efficient and allow the xUnit.Net tests
to run in parallel for better throughput.

- `IntegrationContext` -- if most of the tests will use an out of the box configuration
  (i.e., no fluent interface configuration of any document types), use this base type. Warning though,
  this context type will **not** clean out the main `public` database schema between runs,
  but will delete any existing data
- `DestructiveIntegrationContext` -- similar to `IntegrationContext`, but will wipe out any and all
  Postgresql schema objects in the `public` schema between tests. Use this sparingly please.
- `OneOffConfigurationsContext` -- if a test suite will need to frequently re-configure
  the `DocumentStore`, this context is appropriate. You do *not* need to decorate any of these
  test classes with the `[Collection]` attribute. This fixture will use an isolated schema using the name of the
  test fixture type as the schema name
- `BugIntegrationContext` -- the test harnesses for bugs tend to require custom `DocumentStore`
  configuration, and this context is a specialization of `OneOffConfigurationsContext` for
  the *bugs* schema.
- `StoreFixture` and `StoreContext` are helpful if a series of tests use the same custom
  `DocumentStore` configuration. You'd need to write a subclass of `StoreFixture`, then use
  `StoreContext<YourNewStoreFixture>` as the base class to share the `DocumentStore` between
  test runs with xUnit.Net's shared context (`IClassFixture<T>`)

### Mocha Specs

Refer to the build commands section to look up the commands to run Mocha tests. There is also `npm run tdd` to run the mocha specifications
in a watched mode with growl turned on.

> Note: remember to run `npm install`

### Storyteller Specs

Refer to build commands section to look up the commands to open the StoryTeller editor or run the StoryTeller specs.

### Current Build Matrix

| CI              | .NET | Postgres  |        plv8        | Serializer | 
|-----------------|:----:|:---------:|:------------------:|:----------:|
| GitHub Actions  |  8   |   12.8    | :white_check_mark: |    STJ     | 
| GitHub Actions  |  8   | 15-alpine |        :x:         | Newtonsoft | 
| GitHub Actions  |  7   |   12.8    | :white_check_mark: |  JSON.NET  | 
| GitHub Actions  |  7   |  latest   |        :x:         |    STJ     | 
| Azure Pipelines |  6   |   12.8    | :white_check_mark: |  JSON.NET  | 
| Azure Pipelines |  6   |   12.8    | :white_check_mark: |    STJ     | 
| Azure Pipelines |  6   | 15-alpine |        :x:         |    STJ     | 
| Azure Pipelines |  6   |  latest   |        :x:         | Newtonsoft | 

## Documentation

All the documentation is written in Markdown and the docs are published as a static site hosted in Netlify. v4.x and v3.x use different documentation tools hence are detailed below in separate sub-sections.

### v4.x and above

[VitePress](https://vitepress.vuejs.org/) is used as documentation tool. Along with this, [MarkdownSnippets](https://github.com/SimonCropp/MarkdownSnippets) is used for adding code snippets to docs from source code and [Algolia DocSearch](https://docsearch.algolia.com/) is used for searching the docs via the search box.

The documentation content is the Markdown files in the `/docs` directory directly under the project root. To run the docs locally use `npm run docs` with auto-refresh on any changes.

To add code samples/snippets from the tests in docs, follow the steps below:

Use C# named regions to mark a code block as described in the sample below

```csharp
#region sample_my-snippet
// code sample/snippet
// ...
#endregion
```

All code snippet identifier starts with `sample_` as a convention to clearly identify that the region block corresponds to a sample code/snippet used in docs. Recommend to use kebab case for the identifiers with words in lower case.

Use the below to include the code snippet in a docs page

<pre>
&#60;!-- snippet: sample_my-snippet -->
&#60;!-- endSnippet -->
</pre>

Note that when you run the docs locally, the above placeholder block in the Markdown file will get updated inline with the actual code snippet from the source code. Please commit the changes with the auto-generated inline code snippet as-is after you preview the docs page. This helps with easier change tracking when you send PR's.

Few gotchas:

- Any changes to the code snippets will need to done in the source code. Do not edit/update any of the auto-generated inline code snippet directly in the Markdown files.
- The latest snippet are always pulled into the docs while we publish the docs. Hence do not worry about the inline code snippet in Markdown file getting out of sync with the snippet in source code.

### v3.x

[stdocs](https://www.nuget.org/packages/dotnet-stdocs/) is used as documentation tool. The documentation content is the markdown files in the `/documentation` directory directly under the project root. Any updates to v3.x docs will need to done in [3.14 branch](https://github.com/JasperFx/marten/tree/3.14). To run the documentation website locally with auto-refresh, refer to the build commands section above.

If you wish to insert code samples/snippet to a documentation page from the tests, wrap the code you wish to insert with
`// SAMPLE: name-of-sample` and `// ENDSAMPLE`.
Then to insert that code to the documentation, add `<[sample:name-of-sample]>`.

> Note: content is published to the `gh-pages` branch of this repository. Refer to build commands section to lookup the command for publishing docs.

## License

Copyright © Jeremy D. Miller, Babu Annamalai, Oskar Dudycz, Joona-Pekka Kokko and contributors.

Marten is provided as-is under the MIT license. For more information see [LICENSE](LICENSE).

## Code of Conduct

This project has adopted the code of conduct defined by the [Contributor Covenant](http://contributor-covenant.org/) to clarify expected behavior in our community.
