# Command Line Tooling

::: tip
The command line tooling described on this page is provided by the shared JasperFx infrastructure
libraries, and the [JasperFx command line documentation](https://shared-libs.jasperfx.net/cli/) is the
reference for it: [writing your own commands](https://shared-libs.jasperfx.net/cli/writing-commands),
[arguments and flags](https://shared-libs.jasperfx.net/cli/arguments-flags),
[environment checks](https://shared-libs.jasperfx.net/cli/environment-checks),
[`describe`](https://shared-libs.jasperfx.net/cli/describe) and
[stateful resources](https://shared-libs.jasperfx.net/cli/resources). This page covers what Marten adds.
:::

::: info Native AOT
Since JasperFx 2.101.0 the command line also runs from a Native AOT binary, not just under `dotnet run`,
provided every assembly that contributes commands is built with `JasperFx.SourceGenerator`. Marten is.
See [Native AOT](https://shared-libs.jasperfx.net/cli/#native-aot) in the JasperFx documentation for what
your application needs, and [Publishing Marten with Native AOT](/configuration/aot-publishing#the-command-line-in-the-native-binary).
:::

::: tip
Running an AppHost with .NET Aspire? The optional `JasperFx.Aspire` package
surfaces these same verbs as clickable buttons on each resource tile in the
Aspire dashboard — see
[JasperFx Commands in the Aspire Dashboard](/configuration/aspire-commands).
:::

::: warning
The usage of JasperFx commands shown in this document are only valid for applications bootstrapped with the
[generic host builder](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host) with Marten registered in the application's IoC container.
:::

::: warning
When writing integration tests, make sure to enable host auto-start in your test setup:

```csharp
JasperFxEnvironment.AutoStartHost = true;
```

or in Marten V7 or earlier:

```csharp
OaktonEnvironment.AutoStartHost = true;
```

Without this setting, creating the test server will fail to bootstrap. See also [Creating an Integration Test Harness](https://wolverinefx.net/tutorials/cqrs-with-marten.html#creating-an-integration-test-harness)
:::

Through dependencies on the [JasperFx](https://shared-libs.jasperfx.net/) and
[Weasel](https://weasel.jasperfx.net/) libraries, Marten has support for command line tools to apply
or generate database migrations from the command line. Marten also has support for running or
rebuilding projections from the command line. Lastly, Marten has a more recent command for some
advanced Event Store management features that might be useful in deployment scenarios. 

To use the expanded command line options to a .NET application, add this last line of code shown below to your `Program.cs`:

<!-- snippet: sample_using_webapplication_1 -->
<a id='snippet-sample_using_webapplication_1'></a>
```cs
var builder = WebApplication.CreateBuilder(args);

// Easiest to just do this right after creating builder
// Must be done before calling builder.Build() at least
builder.Host.ApplyJasperFxExtensions();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/MinimalAPI/Program.cs#L10-L18' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_webapplication_1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And finally, use JasperFx as the command line parser and executor by replacing `App.Run()` as the last line of code in your
`Program.cs` file:

<!-- snippet: sample_using_webapplication_2 -->
<a id='snippet-sample_using_webapplication_2'></a>
```cs
// Instead of App.Run(), use the app.RunJasperFxCommands(args)
// as the last line of your Program.cs file
return await app.RunJasperFxCommands(args);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/MinimalAPI/Program.cs#L56-L62' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_webapplication_2' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In your command line in the project directory, you can run:

```bash
dotnet run -- help
```

And you will be given a list of commands.

```bash
The available commands are:

  Alias         Description
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  check-env     Execute all environment checks against the application
  codegen       Utilities for working with JasperFx.CodeGeneration and JasperFx.RuntimeCompiler
  db-apply      Applies all outstanding changes to the database(s) based on the current configuration
  db-assert     Assert that the existing database(s) matches the current configuration
  db-dump       Dumps the entire DDL for the configured Marten database
  db-list       List all database(s) based on the current configuration
  db-patch      Evaluates the current configuration against the database and writes a patch and drop file if there are any differences
  describe      Writes out a description of your running application to either the console or a file
  event-query   Query events across all streams with filters, ordered by sequence ascending, paged
  help          List all the available commands
  marten        Advanced Marten operations to 'heal' event store projection issues or reset data  
  projection-run  Replay a projection over a stream, stream slice, or tag query and show the per-event before/after state
  projections   Asynchronous projection and projection rebuilds
  resources     Check, setup, or teardown stateful resources of this system
  run           Start and run this .Net application
  storage       Administer the Wolverine message storage
  stream-query  Query event stream metadata (version, aggregate type, compaction watermark, archive flag) with filters and paging
```

For any of the listed commands, you can run:

```bash
dotnet run -- help [command]
```

To see more information about the use of that command.

::: info Where the commands come from
The `db-apply`, `db-assert`, `db-dump`, `db-list` and `db-patch` commands are [Weasel](https://weasel.jasperfx.net/cli/)'s, and Marten registers
its databases, every tenant database included, with Weasel so that they see them. Each has its own reference page:
[db-apply](https://weasel.jasperfx.net/cli/db-apply.html), [db-assert](https://weasel.jasperfx.net/cli/db-assert.html),
[db-patch](https://weasel.jasperfx.net/cli/db-patch.html), [db-dump](https://weasel.jasperfx.net/cli/db-dump.html) and
[db-list](https://weasel.jasperfx.net/cli/db-list.html).

The `resources` command is JasperFx's [stateful resource](https://shared-libs.jasperfx.net/cli/resources) model. Marten's database is one
such resource, so `resources setup` applies the same migration `db-apply` does, and `resources check` runs the same comparison as `db-assert`,
but across *every* resource in the application: Wolverine's message storage and message broker objects too. See
[Schema Migrations and Patches](/schema/migrations#choosing-an-approach) for when to reach for which.

The `projections`, `event-query`, `stream-query` and `projection-run` commands come from JasperFx.Events, and work against
any event store registered in the application, Marten's included. See
[Querying the Event Store from the Command Line](#querying-the-event-store-from-the-command-line) below.
:::

## Example Commands

Run these commands in your project's directory.

### List Your Projections

```bash
dotnet run -- projections list
```

### Rebuild Your Projections

To rebuild *all* of your projections:

```bash
dotnet run -- projections rebuild
```

To rebuild a single projection:

```bash
dotnet run -- projections rebuild -p Course
```

(where `Course` is the name of the projection, from the list)

The action (`list`, `rebuild`, `parked` and so on) is the first argument, and options are flags. See
[Command Line Support](/events/projections/async-daemon#command-line-support) in the async daemon documentation for the
rest of the `projections` command.

### Creating a SQL Script from your Marten Database

```sh
dotnet run -- db-dump ./marten.sql
```

### Codegen

::: warning Marten 9.0
Marten 9.0 completely removed its runtime code-generation pipeline (PR [#4461](https://github.com/JasperFx/marten/pull/4461)). **`dotnet run -- codegen write` is no longer necessary for Marten** — there are no Marten artifacts to write or pre-generate. If you committed an `Internal/Generated/` folder pre-9.0, delete it and remove it from `.gitignore`; nothing reads or writes those files anymore.

The `codegen` family of subcommands is still surfaced by the shared JasperFx CLI for other Critter-Stack tools (Wolverine, for example), so the command itself may still run successfully against a host that registers those tools. It just won't do anything on Marten's behalf.
When Wolverine code shares the host, see [CLI: codegen Command](https://shared-libs.jasperfx.net/codegen/cli) for `TypeLoadMode` and the recommended workflow.

See [Runtime code generation removed](/migration-guide#runtime-code-generation-removed) for the full migration story.
:::

## Outside the Dotnet CLI

If you're not using the dotnet CLI yet, you'd just need to compile your new console application like you've always done and call the exe directly. If you're familiar with the *nix style of command-line interfaces ala Git, you should feel right at home with the command line usage in Marten.

For the sake of usability, let's say that you stick a file named "marten.cmd" (or the *nix shell file equivalent) at the root of your codebase like so:

```bash
dotnet run --project src/MyConsoleApp %*
```

All the example above does is delegate any arguments to your console application. Once you have that file, some sample usages are shown below:

Assert that the database matches the current configuration. This command fails if there are differences:

```bash
marten db-assert
```

Update the database to match the application configuration:

```bash
marten db-apply
```

With several databases, such as one per tenant, `db-apply` and `db-assert` walk every one of them. `--parallel N` processes up to
`N` physical databases at once:

```bash
marten db-apply --parallel 4
```

Dump a single file named "database.sql" with all the DDL necessary to build the database to match the application
configuration:

```bash
marten db-dump database.sql
```

Dump the DDL to a folder named "scripts", with a separate file for each feature (each document type, the event store and
so on):

```bash
marten db-dump scripts --by-feature
```

Create a patch file called "patch1.sql", and the matching rollback file "patch1.drop.sql", if any differences are found
between the application configuration and the database. Add `--transactional-script` to wrap the patch in a transaction:

```bash
marten db-patch patch1.sql
```

`db-dump` and `db-patch` work against one database. When the application has more than one, choose it with
`--database` (or `-d`), which takes the database's identifier or its Uri. `db-list` shows what is available:

```bash
marten db-list
marten db-patch tenant1.sql --database tenant1
```

In all cases, the commands expose usage help through `marten help [command]`. Every command also takes the shared host
flags, such as `--environment` to choose the hosting environment and `--log-level` (or `--verbose`) to change how much
logging is written to the console.

## Projections Support

See [the Async Daemon documentation](/events/projections/async-daemon.md) for more information about the newly improved `projections` command,
including a table of its flags and the `floor` action.

## Querying the Event Store from the Command Line {#querying-the-event-store-from-the-command-line}

`event-query`, `stream-query` and `projection-run` read the event store of the application without starting it: they
build the host, but run no hosted services and no async daemon, and they don't append events or save projected
documents. What they have in common:

* `--store <store>` picks the event store when the application registers more than one: its subject uri, such as
  `marten://main` for the store registered with `AddMarten()`, or a bare scheme like `marten` when only one store
  has it. Without it, an application with several event stores gets an error listing them.
* `--tenant <tenant>` scopes the read to one tenant. Leave it off to read across tenants.
* Console logging is turned off, so that nothing interleaves with the output. `--verbose` turns it back on, but only
  with console table output, never with JSON.
* A failure still writes a report in the same shape as a success, with the error in it, so that a script parsing the
  JSON doesn't need a second code path. Because the host is never started, nothing that sets up the database on
  startup has run, so after a failure the command checks the application's
  [stateful resources](https://shared-libs.jasperfx.net/cli/resources) and adds what to run to the report when they
  aren't set up.

### event-query

Pages through events across all streams, in sequence order, with optional filters. The output is JSON unless you ask
for a table with `--format Text`.

| Flag | What it does |
| --- | --- |
| `--stream <id>` | Only this stream's events |
| `--event-type <alias>` | Only events of these type aliases, comma separated, such as `trip_started,trip_ended` |
| `--correlation-id`, `--causation-id`, `--user-name` | Exact match on that metadata column |
| `--timestamp-from`, `--timestamp-to` | Inclusive bounds on the event timestamp, in any `DateTimeOffset` format |
| `--sequence-floor`, `--sequence-ceiling` | Inclusive bounds on the event sequence |
| `--tags <json>` | [Tag](/events/dcb) filter as a JSON object of tag name to value, such as `'{"StudentId":"s-1"}'` |
| `--page <n>`, `--page-size <n>` | 1-based page, default 1, and page size, default 50 |
| `--format Text` | A console table instead of JSON, which is the default (`--format Json`). The value is case sensitive, so `text` is refused |
| `--no-payloads` | Leave out the event bodies |

```bash
dotnet run -- event-query --stream 0b5a8d6e-7a52-4a3a-9f61-2a2f0f3c5d10 --format Text
dotnet run -- event-query --event-type trip_started --timestamp-from 2026-11-01T00:00:00Z --page-size 20
```

### stream-query

Pages through the streams themselves: their version, aggregate type, compaction watermark and archived flag. JSON by
default, `--format Text` for a table.

| Flag | What it does |
| --- | --- |
| `--aggregate-type <type>` | Streams started as this aggregate type, by its simple or full CLR name |
| `--min-version <n>` | Streams with at least this many events |
| `--version-above-compacted <n>` | Streams that have grown by more than this since they were last compacted |
| `--archived <true or false>` | Only archived streams, or only live ones. Omit for both |
| `--created-from`, `--created-to` | Inclusive bounds on when the stream was created |
| `--updated-from`, `--updated-to` | Inclusive bounds on the stream's last append |
| `--where <predicate>` | A Dynamic LINQ predicate over `Id`, `Key`, `Version`, `CompactedVersion`, `IsArchived`, `Created` and `LastTimestamp`, combined with the other filters |
| `--order-by <ordering>` | Such as `"Version desc"`. The default is creation order |
| `--page <n>`, `--page-size <n>` | 1-based page, default 1, and page size, default 50 |
| `--format Text` | A console table instead of JSON, which is the default (`--format Json`). The value is case sensitive, so `text` is refused |

```bash
dotnet run -- stream-query --aggregate-type Trip --min-version 500 --order-by "Version desc" --format Text
dotnet run -- stream-query --where "Version > 100 and not IsArchived"
```

### projection-run

Replays a registered projection over a stream, a slice of a stream, or the events matching some tags, and shows the
projected state before and after each event, for each identity the projection produces. It runs your actual projection
code through the same slicing and grouping as the daemon, but it's stateless: nothing is persisted. The projection
name is the argument, and the output is a console table unless you pass `--json`.

| Flag | What it does |
| --- | --- |
| `--stream <id>` | The stream whose events drive the replay. Required unless you use `--tag` |
| `--from <version>`, `--to <version>` | Only this inclusive range of the stream's versions. Use both or neither |
| `--tag:<name> <value>` | Use the events with this [tag](/events/dcb) instead of a stream. The name is the tag type's CLR name, such as `StudentId`. Repeat it to require several tags. It can't be combined with `--stream`, `--from` or `--to` |
| `--max-events <n>` | The most events to feed into the projection, default 1000. The report says when it was truncated |
| `--json` | Write the result as JSON instead of a table |

```bash
dotnet run -- projection-run Trip --stream 0b5a8d6e-7a52-4a3a-9f61-2a2f0f3c5d10
dotnet run -- projection-run Trip --stream 0b5a8d6e-7a52-4a3a-9f61-2a2f0f3c5d10 --from 10 --to 25 --json
dotnet run -- projection-run EnrollmentSummary --tag:StudentId s-1
```

## Event Store Management or Resetting Data

Some of the operations on `IDocumentStore.Advanced` are now available from the command line for easier usage within
automated deployment scripts. 

If you'll type out:

```bash
dotnet run -- help marten
```

You'll see this output:

```text
marten - Advanced Marten operations to 'heal' event store projection issues or reset data
└── Advanced Marten operations to 'heal' event store projection issues or reset data
    └── dotnet run -- marten 
        ├── [-a, --advance]
        ├── [-c, --correct]
        ├── [-t, --tenant-id <tenantid>]
        ├── [-r, --reset]
        ├── [-v, --env-variable <environmentvariable>]
        ├── [-c, --contentroot <contentroot>]
        ├── [-a, --applicationname <applicationname>]
        ├── [-e, --environment <environment>]
        ├── [-v, --verbose]
        ├── [-l, --log-level Trace|Debug|Information|Warning|Error|Critical|None]
        └── [--config:<prop> <value>]

                                                                                                                                                                          
                                                                  Usage   Description                                                                                     
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                                                        [-a, --advance]   Advance the high water mark to the highest detected point                                       
                                                        [-c, --correct]   Try to correct the event progression based on the event sequence after *some* database hiccups  
                                           [-t, --tenant-id <tenantid>]   Limit the operation to a single tenant if specified                                             
                                                          [-r, --reset]   Reset all Marten data                                                                           
                             [-v, --env-variable <environmentvariable>]   Value in the form <KEY=VALUE> to set an environment variable for this process                   
                                      [-c, --contentroot <contentroot>]   Override the IHostEnvironment.ContentRoot                                                       
                              [-a, --applicationname <applicationname>]   Override the IHostEnvironment.ApplicationName                                                   
                                      [-e, --environment <environment>]   Override the IHostEnvironment.EnvironmentName                                                   
                                                        [-v, --verbose]   Write out much more information at startup and enables console logging                          
  [-l, --log-level Trace|Debug|Information|Warning|Error|Critical|None]   Override the log level                                                                          
                                              [--config:<prop> <value>]   Overwrite individual configuration items   
```
