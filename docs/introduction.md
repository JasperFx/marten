# Introduction

Click this [link](https://sec.ch9.ms/ch9/2d29/a281311a-76bb-4573-a2a0-2dd7affc2d29/S315dotNETconf_high.mp4) to watch an introductory video on Marten.

## What is Marten?

Marten is a library distributed by Nuget that allows .Net developers to use the Postgresql database as both a
[document database](https://en.wikipedia.org/wiki/Document-oriented_database) and a full-featured [event store](https://martinfowler.com/eaaDev/EventSourcing.html) -- with the document database features being the out of the box
mechanism for projected "read side" views of your events. There is absolutely nothing else to install in your application
other than the Nuget or process to run other than Postgresql itself. Marten was made possible by the unique [JSONB](https://www.postgresql.org/docs/current/datatype-json.html) support
first introduced in Postgresql 9.4.

Marten was originally built to replace Raven Db inside a very large web application that was suffering stability and performance issues.
The project name *Marten* came from a quick Google search one day for "what are the natural predators of ravens?" -- which led to us to
use the [marten](https://en.wikipedia.org/wiki/Marten) as our project codename and avatar.

![A Marten](/images/marten.jpeg)

The Marten project was publicly announced in late 2015 and quickly gained a solid community of interested developers. An event sourcing feature set was
added early on, which helped Marten attract much more interest. Marten first went into a production system in 2016 and has been going strong ever since. The V4
release in 2021 marks a massive overhaul of Marten's internals and new functionality requested by our users to better position Marten for the future.


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

Following the common .Net idiom, Marten supplies extension methods to quickly integrate Marten into any .Net Core application that uses the `IServiceCollection` abstractions to register IoC services.

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

See [integrating Marten in .NET Core applications](/configuration/) for more information and options about this integration.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L17-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_user_document' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

_For more information on document identity, see [identity](/documents/identity)._

And now that we've got a PostgreSQL schema and an `IDocumentStore` variable called `store`, let's start persisting and loading user documents:

<!-- snippet: sample_opening_sessions -->
<a id='snippet-sample_opening_sessions'></a>
```cs
// Open a session for querying, loading, and
// updating documents
using (var session = store.LightweightSession())
{
    var user = new User { FirstName = "Han", LastName = "Solo" };
    session.Store(user);

    await session.SaveChangesAsync();
}

// Open a session for querying, loading, and
// updating documents with a backing "Identity Map"
using (var session = store.QuerySession())
{
    var existing = await session
        .Query<User>()
        .SingleAsync(x => x.FirstName == "Han" && x.LastName == "Solo");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L49-L68' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_opening_sessions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now that we've got a document store, we can use that to create a new `IQuerySession` object just for querying or loading documents from the database:

<!-- snippet: sample_start_a_query_session -->
<a id='snippet-sample_start_a_query_session'></a>
```cs
using (var session = store.QuerySession())
{
    var internalUsers = session
        .Query<User>().Where(x => x.Internal).ToArray();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L41-L47' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start_a_query_session' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For more information on the query support within Marten, check [document querying](/documents/querying/)



There is a lot more capabilities than what we're showing here, so head on over to the table of contents on the sidebar to see what else Marten offers.

## Working with Events

TODO -- just a quickstart!
