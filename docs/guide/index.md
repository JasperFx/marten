# Introduction

Click this [link](https://sec.ch9.ms/ch9/2d29/a281311a-76bb-4573-a2a0-2dd7affc2d29/S315dotNETconf_high.mp4) to watch an introductory video on Marten.

## What is Marten?

Marten is a library distributed by Nuget that allows .Net developers to use the Postgresql database as both a
document database and a full-featured [event store](https://martinfowler.com/eaaDev/EventSourcing.html) -- with the document database features being the out of the box
mechanism for projected "read side" views of your events. There is absolutely nothing else to install in your application
other than the Nuget or process to run other than Postgresql itself.

The developers behind Marten strongly feel that [document databases](https://en.wikipedia.org/wiki/Document-oriented_database) are oftn more efficient
for many software development projects than using the more common RDBMS with an ORM
tool like Entity Framework Core or Dapper. We also like having a solution for event sourcing that is all "in the box" with both event
capture and integrated read-side projection support in one tool.

Postgresql v9.4 added very strong support for efficiently storing, querying, and manipulating JSON data. At about the same time, the original
Marten shop was struggling with an early version of RavenDb in production. After familiarizing ourselves with Postgresql's new
[JSONB](https://www.postgresql.org/docs/current/datatype-json.html) support, we conceived of what became Marten as a library that would
mimic the basic RavenDb API usage, but sit on top of the very robust Postgresql database engine.

The project name came from a quick Google search one day for "what are the natural predators of ravens?" -- which led to us
using the [marten](https://en.wikipedia.org/wiki/Marten) as our project codename and avatar.

![A Marten](/images/marten.jpeg)

When we announced the project publicly in late 2015, it quickly gained a solid community of interested developers. An event sourcing feature set was
added early on, which helped Marten attract many more developers. Marten first went into a production system in 2016 and has been going strong ever since.



## .NET version compatibility

Marten aligns with the [.NET Core Support Lifecycle](https://dotnet.microsoft.com/platform/support/policy/dotnet-core) to determine platform compatibility.

4.xx targets `netstandard2.0` & `net5.0` and is compatible with `.NET Core 2.x`, `.NET Core 3.x` and `.NET 5+`.

::: tip INFO
.NET Framework support was dropped as part of the v4 release, if you require .NET Framework support, please use the latest Marten 3.xx release.
:::

## Marten Quickstart

::: tip INFO
There's a very small [sample project in the Marten codebase](https://github.com/JasperFx/marten/tree/master/src/AspNetCoreWithMarten) that shows the mechanics for wiring
Marten into a .Net Core application.
:::

By popular demand, Marten 3.12 added extension methods to quickly integrate Marten into any .Net Core application that uses the `IServiceCollection` abstractions to register IoC services.

In the `Startup.ConfigureServices()` method of your .Net Core application (or you can use `IHostBuilder.ConfigureServices()` as well) make a call to `AddMarten()` to register Marten services like so:

<!-- snippet: sample_StartupConfigureServices -->
<a id='snippet-sample_startupconfigureservices'></a>
```cs
public class Startup
{
    public IConfiguration Configuration { get; }
    public IHostEnvironment Environment { get; }

    public Startup(IConfiguration configuration, IHostEnvironment environment)
    {
        Configuration = configuration;
        Environment = environment;
    }

    // This method gets called by the runtime. Use this method to add services to the container.
    // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
    public void ConfigureServices(IServiceCollection services)
    {
        // This is the absolute, simplest way to integrate Marten into your
        // .Net Core application with Marten's default configuration
        services.AddMarten(options =>
        {
            // Establish the connection string to your Marten database
            options.Connection(Configuration.GetConnectionString("Marten"));

            // If we're running in development mode, let Marten just take care
            // of all necessary schema building and patching behind the scenes
            if (Environment.IsDevelopment())
            {
                options.AutoCreateSchemaObjects = AutoCreate.All;
            }
        });
    }

    // and other methods we don't care about right now...
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Startup.cs#L13-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_startupconfigureservices' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

TODO -- reference DocumentStore and IDocumentSession / IQuerySession

See [integrating Marten in .NET Core applications](/guide/integration) for more information and options about this integration.

Also see the blog post [Marten, the Generic Host Builder in .Net Core, and why this could be the golden age for OSS in .Net](https://jeremydmiller.com/2021/07/29/marten-the-generic-host-builder-in-net-core-and-why-this-could-be-the-golden-age-for-oss-in-net/) for more background about how Marten is fully embracing
the generic host in .Net. 


## Working with Documents

Now, for your first document type, let's represent the users in our system:

<!-- snippet: sample_user_document -->
<a id='snippet-sample_user_document'></a>
```cs
public class User
{
    public Guid Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public bool Internal { get; set; }
    public string UserName { get; set; }
    public string Department { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L15-L26' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_user_document' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

_For more information on document id's, see [identity](/guide/documents/identity/)._

And now that we've got a PostgreSQL schema and an `IDocumentStore`, let's start persisting and loading user documents:

<!-- snippet: sample_opening_sessions -->
<a id='snippet-sample_opening_sessions'></a>
```cs
// Open a session for querying, loading, and
// updating documents
using (var session = store.LightweightSession())
{
    var user = new User { FirstName = "Han", LastName = "Solo" };
    session.Store(user);

    session.SaveChanges();
}

// Open a session for querying, loading, and
// updating documents with a backing "Identity Map"
using (var session = store.OpenSession())
{
    var existing = session
        .Query<User>()
        .Where(x => x.FirstName == "Han" && x.LastName == "Solo")
        .Single();
}

// Open a session for querying, loading, and
// updating documents that performs automated
// "dirty" checking of previously loaded documents
using (var session = store.DirtyTrackedSession())
{
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L47-L74' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_opening_sessions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->



There is a lot more capabilities than what we're showing here, so head on over to the table of contents on the sidebar to see what else Marten offers.

## Working with Events

TODO -- just a quickstart!
