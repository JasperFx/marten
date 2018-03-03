# Marten 
## Polyglot Persistence Powered by .NET and PostgreSQL

[![Join the chat at https://gitter.im/JasperFx/Marten](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/JasperFx/Marten?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)
[![Windows Build Status](https://ci.appveyor.com/api/projects/status/github/jasperfx/marten?svg=true)](https://ci.appveyor.com/project/jasper-ci/marten)
[![Linux Build status](https://api.travis-ci.org/JasperFx/marten.svg)](https://travis-ci.org/JasperFx/marten)
[![Nuget Package](https://img.shields.io/nuget/v/Marten.svg?style=flat)](https://www.nuget.org/packages/Marten/)

![marten logo](http://jasperfx.github.io/marten/content/images/banner.png)


The Marten library provides .NET developers with the ability to use the proven [PostgreSQL database engine](http://www.postgresql.org/) and its [fantastic JSON support](https://www.compose.io/articles/is-postgresql-your-next-json-database/) as a fully fledged [document database](https://en.wikipedia.org/wiki/Document-oriented_database). The Marten team believes that a document database has far reaching benefits for developer productivity over relational databases with or without an ORM tool.

Marten also provides .NET developers with an ACID-compliant event store with user-defined projections against event streams.

## Working with the Code

Before getting started you will need the following in your environment:

* Access to a PostgreSQL **9.5+** database.
* An environment variable of `marten_testing_database` set to the connection string for the database you want to use as a testbed. (See the [Npgsql documentation](http://www.npgsql.org/doc/connection-string-parameters.html) for more information about PostgreSQL connection strings )
* You will also need to enable the PLV8 extension inside of PostgreSQL for running JavaScript stored procedures for the nascent projection support. See
[this link](http://www.postgresonline.com/journal/archives/360-PLV8-binaries-for-PostgreSQL-9.5-windows-both-32-bit-and-64-bit.html) for pre-built binaries for PLV8 running on Windows
* You will also need to make sure that the login you are using to connect to your databasee is a member of the `postgres` role
* Ensure you have installed [.NET Core SDK 2.0](https://www.microsoft.com/net/download/core)
* Once you have the codebase and the connection string file, run the rake script or use the dotnet CLI to restore and build the solution.

You are now ready to contribute to Marten.

### Tooling

* Unit Tests rely on [xUnit](http://xunit.github.io/) and [Shouldly](https://github.com/shouldly/shouldly)
* Rake is used for build automation. _It is not mandatory for development_.
* [Node.js](https://nodejs.org/en/) runs our Mocha specs.
* [Storyteller](http://storyteller.github.io) for some of the data intensive automated tests

### Mocha Specs

To run mocha tests use `rake mocha` or `npm run test`. There is also `npm run tdd` to run the mocha specifications
in a watched mode with growl turned on. 

> Note: remember to run `npm install`

### Storyteller Specs

To open the Storyteller editor, use the command `rake open_st` from the command line or `rake storyeller` to run the Storyteller specs. If you don't want to use rake, you can launch the
Storyteller editor *after compiling the solution* by the command `packages\storyteller\tools\st.exe open src/Marten.Testing`.

### Documentation

The documentation content is the markdown files in the `/documentation` directory directly under the project root. To run the documentation website locally with auto-refresh, either use the rake task `rake docs` or the batch script named `run-docs.cmd`. 

If you wish to insert code samples to a documentation page from the tests, wrap the code you wish to insert with
`// SAMPLE: name-of-sample` and `// ENDSAMPLE`.
Then to insert that code to the documentation, add `<[sample:name-of-sample]>`.

> Note: content is published to the `gh-pages` branch of this repository by running the `publish-docs.cmd` command.

### Rake Commands

```
# run restore, build and test
rake

# run all tests including mocha tests
rake test

# running documentation website locally
rake docs
```

### DotNet CLI Commands

```
# restore nuget libraries
dotnet restore src\Marten.sln

# build solution
dotnet build src\Marten.sln

# running tests for a specific target framework
dotnet test src\Marten.Testing\Marten.Testing.csproj --framework netcoreapp2.0

# mocha tests
npm install
npm run test

# running documentation website locally
dotnet restore docs.csproj
dotnet stdocs run
```
