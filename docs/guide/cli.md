# Command Line Tooling for Marten Management

::: warning
As of v4.0, the usage of Marten.CommandLine shown in this document is only valid for applications bootstrapped with the .Net Core / .Net 5.0
[generic host builder](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-3.1) with Marten registered in the application's IoC container.
:::

There is a separate NuGet package called _Marten.CommandLine_ that can be used to quickly add command-line tooling directly to
your .Net Core application that uses Marten. _Marten.CommandLine_ is an extension library to [Oakton](https://jasperfx.github.io/oakton).

To use the expanded command line options to a .Net Core application bootstrapped by `IHostBuilder`, add a reference to the _Marten.CommandLine_ Nuget and ever so slightly change your `Program.Main()` entry point as shown below:

<!-- snippet: sample_SampleConsoleApp -->
<a id='snippet-sample_sampleconsoleapp'></a>
```cs
public class Program
{
    // It's actually important to return Task<int>
    // so that the application commands can communicate
    // success or failure
    public static Task<int> Main(string[] args)
    {
        return CreateHostBuilder(args)

            // This line replaces Build().Start()
            // in most dotnet new templates
            .RunOaktonCommands(args);
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Program.cs#L13-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sampleconsoleapp' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Once the _Marten.CommandLine_ Nuget is installed and Oakton is handling your command line parsing, you should be able to see the Marten commands by typing `dotnet run -- help` in the command line terminal of your choice at the root of your project:

```bash
  ----------------------------------------------------------------------------------------------------------
    Available commands:
  ----------------------------------------------------------------------------------------------------------
        check-env -> Execute all environment checks against the application
         describe -> Writes out a description of your running application to either the console or a file
             help -> list all the available commands
     marten-apply -> Applies all outstanding changes to the database based on the current configuration
    marten-assert -> Assert that the existing database matches the current Marten configuration
      marten-dump -> Dumps the entire DDL for the configured Marten database
     marten-patch -> Evaluates the current configuration against the database and writes a patch and drop file if there are any differences
      projections -> Rebuilds all projections of specified kind
              run -> Start and run this .Net application
  ----------------------------------------------------------------------------------------------------------
```

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

## Current Thinking about Marten + Sqitch
Our team doing the RavenDB-to-Marten transition work has turned us on to using [Sqitch](http://sqitch.org/) for database migrations. From my point of view, I like this choice because Sqitch just uses script files in whatever the underlying database's SQL dialect is. That means that Marten can use our existing `WritePatch()` <[linkto:documentation/schema/migrations;title=schema management]> to tie into Sqitch's migration scheme.

The way that I think this could work for us is first to have a Sqitch project established in our codebase with its folders for updates, rollbacks, and verify's. In our build script that runs in our master continuous integration (CI) build, we would:

1. Call sqitch to update the CI database (or whatever database we declare to be the source of truth) with the latest known migrations
2. Call the `marten-assert` command shown above to detect if there are outstanding differences between the application configuration and the database by examining the exit code from that command
3. If there are any differences detected, figure out what the next migration name would be based on our naming convention and use sqitch to start a new migration with that name
4. Run the `marten-patch` command to write the update and rollback scripts to the file locations previously determined in steps 2 & 3
5. Commit the new migration file back to the underlying Git repository

I'm insisting on doing this on our CI server instead of making developers do it locally because I think it'll lead to less duplicated work and fewer problems from these migrations being created against work in progress feature branches.

For production (and staging/QA) deployments, we'd just use sqitch out of the box to bring the databases up to date.

I like this approach because it keeps the monotony of repetitive database change tracking out of our developer's hair, while also allowing them to integrate database changes from outside of Marten objects into the database versioning.
