# Command Line Tooling

::: warning
The usage of Marten.CommandLine shown in this document is only valid for applications bootstrapped with the
[generic host builder](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host) with Marten registered in the application's IoC container.
:::

There is a separate NuGet package called _Marten.CommandLine_ that can be used to quickly add command-line tooling directly to
your .Net Core application that uses Marten. _Marten.CommandLine_ is an extension library to [Oakton](https://jasperfx.github.io/oakton) that
is the actual command line parser in this case.

To use the expanded command line options to a .NET application, add a reference to the _Marten.CommandLine_ Nuget and add this line of code to your `Program.cs`:

<!-- snippet: sample_using_WebApplication_1 -->
<a id='snippet-sample_using_webapplication_1'></a>
```cs
var builder = WebApplication.CreateBuilder(args);

// Easiest to just do this right after creating builder
// Must be done before calling builder.Build() at least
builder.Host.ApplyJasperFxExtensions();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/MinimalAPI/Program.cs#L10-L18' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_webapplication_1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And finally, use Oakton as the command line parser and executor by replacing `App.Run()` as the last line of code in your
`Program.cs` file:

<!-- snippet: sample_using_WebApplication_2 -->
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
dotnet -- help
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
  help          List all the available commands
  projections   Asynchronous projection and projection rebuilds
  resources     Check, setup, or teardown stateful resources of this system
  run           Start and run this .Net application
  storage       Administer the Wolverine message storage
```

For any of the listed commands, you can run:

```bash
dotnet -- help [command]
```

To see more information about the use of that command.

## Example Commands

Run these commands in your project's directory.

### List Your Projections

```bash
dotnet run -- projections list
```

### Rebuild Your Projections

To rebuild _all_ of your projections:

```bash
dotnet run -- projections rebuild
```
To rebuild a single projection:

```bash
 dotnet run -- projections -p Course rebuild
```
(where `Course` is the name of the projection, from the list)

### Creating a SQL Script from your Marten Database

```sh
dotnet run -- db-dump -d Marten ./marten.sql
```

### Codegen

You can use the CLI to preview, generate, write, or test the code generation:

To Test Codegen:

```bash
dotnet run -- codegen test
```

To preview codegen:

```bash
dotnet run -- codegen preview
```

To write the codegen to your project (in the `internal/codegen` directory):

```bash
dotnet run -- codegen write
```

## Outside the Dotnet CLI

If you're not using the dotnet CLI yet, you'd just need to compile your new console application like you've always done and call the exe directly. If you're familiar with the *nix style of command-line interfaces ala Git, you should feel right at home with the command line usage in Marten.

For the sake of usability, let's say that you stick a file named "marten.cmd" (or the *nix shell file equivalent) at the root of your codebase like so:

```bash
dotnet run --project src/MyConsoleApp %*
```

All the example above does is delegate any arguments to your console application. Once you have that file, some sample usages are shown below:

Assert that the database matches the current database. This command will fail if there are differences

```bash
marten marten-assert --log log.txt
```

This command tries to update the database to reflect the application configuration

```bash
marten marten-apply --log log.txt
```

This dumps a single file named "database.sql" with all the DDL necessary to build the database to
match the application configuration

```bash
marten marten-dump database.sql
```

This dumps the DDL to separate files per document
type to a folder named "scripts"

```bash
marten marten-dump scripts --by-type
```

Create a patch file called "patch1.sql" and
the corresponding rollback file "patch.drop.sql" if any
differences are found between the application configuration
and the database

```bash
marten marten-patch patch1.sql --drop patch1.drop.sql
```

In all cases, the commands expose usage help through "marten help [command]." Each of the commands also exposes a "--conn" (or "-c" if you prefer) flag to override the database connection string and a "--log" flag to record all the command output to a file.

## Projections Support

See [the Async Daemon documentation](/events/projections/async-daemon.md) for more information about the newly improved `projections` command.
