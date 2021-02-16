# Command Line Tooling for Marten Management

::: warning
As of v4.0, the usage of Marten.CommandLine shown in this document is only valid for applications bootstrapped with the .Net Core
[generic host builder](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-3.1) with Marten registered in the application's IoC container.
:::

There is a separate NuGet package called _Marten.CommandLine_ that can be used to quickly add command-line tooling directly to
your .Net Core application that uses Marten. _Marten.CommandLine_ is an extension library to [Oakton.AspNetCore](https://jasperfx.github.io/oakton/documentation/aspnetcore/).

To use the expanded command line options to a .Net Core application bootstrapped by `IHostBuilder`, add a reference to the _Marten.CommandLine_ Nuget and ever so slightly change your `Program.Main()` entry point as shown below:

<<< @/../src/AspNetCoreWithMarten/Program.cs#sample_SampleConsoleApp

If you named your application "marten.exe", the commands supported are these:

```bash
------------------------------------------------------------------------------------------------------------------------------------

  Available commands:
------------------------------------------------------------------------------------------------------------------------------------

   apply -> Applies all outstanding changes to the database based on the current configuration
  assert -> Assert that the existing database matches the current Marten configuration
    dump -> Dumps the entire DDL for the configured Marten database
   patch -> Evaluates the current configuration against the database and writes a patch and drop file if there are any differences
------------------------------------------------------------------------------------------------------------------------------------
```

If you're not using the dotnet CLI yet, you'd just need to compile your new console application like you've always done and call the exe directly. If you're familiar with the *nix style of command-line interfaces ala Git, you should feel right at home with the command line usage in Marten.

For the sake of usability, let's say that you stick a file named "marten.cmd" (or the *nix shell file equivalent) at the root of your codebase like so:

```bash
dotnet run --project src/MyConsoleApp %*
```

All the example above does is delegate any arguments to your console application. Once you have that file, some sample usages are shown below:

Assert that the database matches the current database. This command will fail if there are differences

```bash
marten assert --log log.txt
```

This command tries to update the database to reflect the application configuration

```bash
marten apply --log log.txt
```

This dumps a single file named "database.sql" with all the DDL necessary to build the database to
match the application configuration

```bash
marten dump database.sql
```

This dumps the DDL to separate files per document
type to a folder named "scripts"

```bash
marten dump scripts --by-type
```

Create a patch file called "patch1.sql" and
the corresponding rollback file "patch.drop.sql" if any
differences are found between the application configuration
and the database

```bash
marten patch patch1.sql --drop patch1.drop.sql
```

In all cases, the commands expose usage help through "marten help [command]." Each of the commands also exposes a "--conn" (or "-c" if you prefer) flag to override the database connection string and a "--log" flag to record all the command output to a file.

## Current Thinking about Marten + Sqitch
Our team doing the RavenDB-to-Marten transition work has turned us on to using [Sqitch](http://sqitch.org/) for database migrations. From my point of view, I like this choice because Sqitch just uses script files in whatever the underlying database's SQL dialect is. That means that Marten can use our existing `WritePatch()` <[linkto:documentation/schema/migrations;title=schema management]> to tie into Sqitch's migration scheme.

The way that I think this could work for us is first to have a Sqitch project established in our codebase with its folders for updates, rollbacks, and verify's. In our build script that runs in our master continuous integration (CI) build, we would:

1. Call sqitch to update the CI database (or whatever database we declare to be the source of truth) with the latest known migrations
2. Call the `marten assert` command shown above to detect if there are outstanding differences between the application configuration and the database by examining the exit code from that command
3. If there are any differences detected, figure out what the next migration name would be based on our naming convention and use sqitch to start a new migration with that name
4. Run the `marten patch` command to write the update and rollback scripts to the file locations previously determined in steps 2 & 3
5. Commit the new migration file back to the underlying Git repository

I'm insisting on doing this on our CI server instead of making developers do it locally because I think it'll lead to less duplicated work and fewer problems from these migrations being created against work in progress feature branches.

For production (and staging/QA) deployments, we'd just use sqitch out of the box to bring the databases up to date.

I like this approach because it keeps the monotony of repetitive database change tracking out of our developer's hair, while also allowing them to integrate database changes from outside of Marten objects into the database versioning.
