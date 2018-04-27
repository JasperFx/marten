<!--title:Command Line Tooling for Marten Management-->


There is a separate NuGet package called _Marten.CommandLine_ that can be used to quickly generate your own command-line tooling to
use for managing Marten schemas at development time. In usage, you would create a new .NET console application in your system's
solution that would reference the _Marten.CommandLine_ nuget and all of the relevant libraries from your own system. 

Once you have the project and dependencies set up, setting up your command line tool is just this:

<[sample:SampleConsoleApp]>

Replace the method `configureStoreOptions()` with whatever your application does to bootstrap and configure Marten. Doing this exposes 
commands to write or apply schema patches and verify an existing schema against the application configuration. If you named your
application "marten.exe", the commands supported in 1.0 are these:

<pre>
------------------------------------------------------------------------------------------------------------------------------------

  Available commands:
------------------------------------------------------------------------------------------------------------------------------------

   apply -> Applies all outstanding changes to the database based on the current configuration
  assert -> Assert that the existing database matches the current Marten configuration
    dump -> Dumps the entire DDL for the configured Marten database
   patch -> Evaluates the current configuration against the database and writes a patch and drop file if there are any differences
------------------------------------------------------------------------------------------------------------------------------------
</pre>

If you're not using the dotnet CLI yet, you'd just need to compile your new console application like you've always done and call the exe directly. If you're familiar with the *nix style of command-line interfaces ala Git, you should feel right at home with the command line usage in Marten.

For the sake of usability, let's say that you stick a file named "marten.cmd" (or the *nix shell file equivalent) at the root of your codebase like so:
<pre>
dotnet run --project src/MyConsoleApp %*
</pre>

All the example above does is delegate any arguments to your console application. Once you have that file, some sample usages are shown below:


Assert that the database matches the current database. This command will fail if there are differences

    marten assert --log log.txt

This command tries to update the database to reflect the application configuration

    marten apply --log log.txt

This dumps a single file named "database.sql" with all the DDL necessary to build the database to
match the application configuration

    marten dump database.sql

This dumps the DDL to separate files per document
type to a folder named "scripts"

    marten dump scripts --by-type

Create a patch file called "patch1.sql" and
the corresponding rollback file "patch.drop.sql" if any
differences are found between the application configuration
and the database

    marten patch patch1.sql --drop patch1.drop.sql

In all cases, the commands expose usage help through "marten help [command]." Each of the commands also exposes a "--conn" (or "-c" if you prefer) flag to override the database connection string and a "--log" flag to record all the command output to a file.

### Current Thinking about Marten + Sqitch
Our team doing the RavenDB-to-Marten transition work has turned us on to using [Sqitch](http://sqitch.org/) for database migrations. From my point of view, I like this choice because Sqitch just uses script files in whatever the underlying database's SQL dialect is. That means that Marten can use our [existing `WritePatch()` schema management](http://jasperfx.github.io/marten/documentation/schema/migrations/) to tie into Sqitch's migration scheme.

The way that I think this could work for us is first to have a Sqitch project established in our codebase with its folders for updates, rollbacks, and verify's. In our build script that runs in our master continuous integration (CI) build, we would:

1. Call sqitch to update the CI database (or whatever database we declare to be the source of truth) with the latest known migrations
2. Call the `marten assert` command shown above to detect if there are outstanding differences between the application configuration and the database by examining the exit code from that command
3. If there are any differences detected, figure out what the next migration name would be based on our naming convention and use sqitch to start a new migration with that name
4. Run the `marten patch` command to write the update and rollback scripts to the file locations previously determined in steps 2 & 3
5. Commit the new migration file back to the underlying Git repository

I'm insisting on doing this on our CI server instead of making developers do it locally because I think it'll lead to less duplicated work and fewer problems from these migrations being created against work in progress feature branches.

For production (and staging/QA) deployments, we'd just use sqitch out of the box to bring the databases up to date.

I like this approach because it keeps the monotony of repetitive database change tracking out of our developer's hair, while also allowing them to integrate database changes from outside of Marten objects into the database versioning.
