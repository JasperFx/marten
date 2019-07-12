# Marten 
## Polyglot Persistence Powered by .NET and PostgreSQL

[![Join the chat at https://gitter.im/JasperFx/Marten](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/JasperFx/Marten?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)
[![Windows Build Status](https://ci.appveyor.com/api/projects/status/github/jasperfx/marten?svg=true)](https://ci.appveyor.com/project/jasper-ci/marten)
[![Linux Build status](https://dev.azure.com/jasperfx-marten/marten/_apis/build/status/marten?branchName=master)](https://dev.azure.com/jasperfx-marten/marten/_build/latest?definitionId=1&branchName=master)
[![Nuget Package](https://badgen.net/nuget/v/marten)](https://www.nuget.org/packages/Marten/)

![marten logo](http://jasperfx.github.io/marten/content/images/banner.png)

The Marten library provides .NET developers with the ability to use the proven [PostgreSQL database engine](http://www.postgresql.org/) and its [fantastic JSON support](https://www.compose.io/articles/is-postgresql-your-next-json-database/) as a fully fledged [document database](https://en.wikipedia.org/wiki/Document-oriented_database). The Marten team believes that a document database has far reaching benefits for developer productivity over relational databases with or without an ORM tool.

Marten also provides .NET developers with an ACID-compliant event store with user-defined projections against event streams.

## Working with the Code

Before getting started you will need the following in your environment:

**1. .NET Core SDK 2.1**

Available [here](https://www.microsoft.com/net/download/core)

**2. PostgreSQL **9.5+** database with PLV8**

You need to enable the PLV8 extension inside of PostgreSQL for running JavaScript stored procedures for the nascent projection support.

Ensure the following:

- The login you are using to connect to your database is a member of the `postgres` role
- An environment variable of `marten_testing_database` is set to the connection string for the database you want to use as a testbed. (See the [Npgsql documentation](http://www.npgsql.org/doc/connection-string-parameters.html) for more information about PostgreSQL connection strings ).

_Help with PSQL/PLV8_

- On Windows, see [this link](http://www.postgresonline.com/journal/archives/360-PLV8-binaries-for-PostgreSQL-9.5-windows-both-32-bit-and-64-bit.html) for pre-built binaries of PLV8
- On *nix, check [marten-local-db](https://github.com/eouw0o83hf/marten-local-db) for a Docker based PostgreSQL instance including PLV8.

Once you have the codebase and the connection string file, run the rake script or use the dotnet CLI to restore and build the solution.

You are now ready to contribute to Marten.

### Tooling

* Unit Tests rely on [xUnit](http://xunit.github.io/) and [Shouldly](https://github.com/shouldly/shouldly)
* [Bullseye](https://github.com/adamralph/bullseye) is used for build automation.
* [Node.js](https://nodejs.org/en/) runs our Mocha specs.
* [Storyteller](http://storyteller.github.io) for some of the data intensive automated tests

### Build Commands

Description | Windows Commandline | PowerShell | Linux Shell | DotNet CLI
---|---|---|---|---
Run restore, build and test | `build.cmd` | `build.ps1`  | `build.sh` | `dotnet build src\Marten.sln`
Run all tests including mocha tests | `build.cmd test` | `build.ps1 test` | `build.sh test` | `dotnet run -p martenbuild.csproj -- test`
Run just mocha tests | `build.cmd mocha` | `build.ps1 mocha` | `build.sh mocha` | `dotnet run -p martenbuild.csproj -- mocha`
Run StoryTeller tests | `build.cmd storyteller` | `build.ps1 storyteller` | `build.sh storyteller` | `dotnet run -p martenbuild.csproj -- storyteller`
Open StoryTeller editor | `build.cmd open_st` | `build.ps1 open_st` | `build.sh open_st` | `dotnet run -p martenbuild.csproj -- open_st`
Run documentation website locally | `build.cmd docs` | `build.ps1 docs` | `build.sh docs` | `dotnet run -p martenbuild.csproj -- docs`
Publish docs | `build.cmd publish-docs` | `build.ps1 publish-docs` | `build.sh publish-docs` | `dotnet run -p martenbuild.csproj -- publish-docs`
Run benchmarks | `build.cmd benchmarks` | `build.ps1 benchmarks` | `build.sh benchmarks` | `dotnet run -p martenbuild.csproj -- benchmarks`

> Note: You should have a running Postgres instance while running unit tests or StoryTeller tests.

### Mocha Specs

Refer to the build commands section to look up the commands to run Mocha tests. There is also `npm run tdd` to run the mocha specifications
in a watched mode with growl turned on. 

> Note: remember to run `npm install`

### Storyteller Specs

Refer to build commands section to look up the commands to open the StoryTeller editor or run the StoryTeller specs.

### Documentation

The documentation content is the markdown files in the `/documentation` directory directly under the project root. To run the documentation website locally with auto-refresh, refer to the build commands section above.

If you wish to insert code samples to a documentation page from the tests, wrap the code you wish to insert with
`// SAMPLE: name-of-sample` and `// ENDSAMPLE`.
Then to insert that code to the documentation, add `<[sample:name-of-sample]>`.

> Note: content is published to the `gh-pages` branch of this repository. Refer to build commands section to lookup the command for publishing docs.
