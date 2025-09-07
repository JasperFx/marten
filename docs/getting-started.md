# Getting Started

Following the common .NET idiom, Marten supplies extension methods to quickly integrate Marten into any .NET application that uses the `IServiceCollection` abstractions to register IoC services. 
Most features of Marten will work without the IoC registrations or any kind of `IHostBuilder`, but all of the command line tooling and
much of the "Async Daemon" or database setup activators leverage the basic .NET `IHost` model. See the section on using `DocumentStore` directly
if you need to use Marten without a full .NET `IHost`.

To add Marten to a .NET project, first go get the Marten library from Nuget:

::: code-group

```shell [.NET CLI]
dotnet add package Marten
```

```powershell [Powershell]
PM> Install-Package Marten
```

```shell [Paket]
dotnet paket add Marten
```

:::

In the startup of your .NET application, make a call to `AddMarten()` to register Marten services like so:

<!-- snippet: sample_StartupConfigureServices -->
<a id='snippet-sample_StartupConfigureServices'></a>
```cs
// This is the absolute, simplest way to integrate Marten into your
// .NET application with Marten's default configuration
builder.Services.AddMarten(options =>
{
    // Establish the connection string to your Marten database
    options.Connection(builder.Configuration.GetConnectionString("Marten")!);

    // If you want the Marten controlled PostgreSQL objects
    // in a different schema other than "public"
    options.DatabaseSchemaName = "other";

    // There are of course, plenty of other options...
})

// This is recommended in new development projects
.UseLightweightSessions()

// If you're using Aspire, use this option *instead* of specifying a connection
// string to Marten
.UseNpgsqlDataSource();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Program.cs#L16-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_StartupConfigureServices' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

See [Bootstrapping with HostBuilder](/configuration/hostbuilder) for more information and options about this integration.

::: tip INFO
Use of the `.AddMarten` integration is not mandatory, see [Creating a standalone store](#creating-a-standalone-store)
:::

## PostgreSQL

The next step is to get access to a PostgreSQL database schema. If you want to let Marten build database schema objects on the fly at development time,
make sure that your user account has rights to execute `CREATE TABLE/FUNCTION` statements.

Marten uses the [Npgsql](http://www.npgsql.org) library to access PostgreSQL from .NET, so you'll likely want to read their [documentation on connection string syntax](http://www.npgsql.org/doc/connection-string-parameters.html).

## Working with Documents

Now, for your first document type, we'll represent the users in our system:

<!-- snippet: sample_GettingStartedUser -->
<a id='snippet-sample_GettingStartedUser'></a>
```cs
public class User
{
    public Guid Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }

    public bool Internal { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/User.cs#L3-L12' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_GettingStartedUser' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

*For more information on document identity, see [identity](/documents/identity).*

::: tip
If you registered Marten with `AddMarten()`, the `IDocumentSession` and `IQuerySession` services are registered with a `Scoped`
lifetime. You should just inject a session directly in most cases. `IDocumentStore` is registered with `Singleton` scope, but 
you'll rarely need to interact with that service.
:::

From here, an instance of `IDocumentStore` or a type of `IDocumentSession` can be injected into the class/controller/endpoint of your choice and we can start persisting and loading user documents:

<!-- snippet: sample_UserEndpoints -->
<a id='snippet-sample_UserEndpoints'></a>
```cs
// You can inject the IDocumentStore and open sessions yourself
app.MapPost("/user",
    async (CreateUserRequest create,

    // Inject a session for querying, loading, and updating documents
    [FromServices] IDocumentSession session) =>
{
    var user = new User {
        FirstName = create.FirstName,
        LastName = create.LastName,
        Internal = create.Internal
    };
    session.Store(user);

    // Commit all outstanding changes in one
    // database transaction
    await session.SaveChangesAsync();
});

app.MapGet("/users",
    async (bool internalOnly, [FromServices] IDocumentSession session, CancellationToken ct) =>
{
    return await session.Query<User>()
        .Where(x=> x.Internal == internalOnly)
        .ToListAsync(ct);
});

// OR use the lightweight IQuerySession if all you're doing is running queries
app.MapGet("/user/{id:guid}",
    async (Guid id, [FromServices] IQuerySession session, CancellationToken ct) =>
{
    return await session.LoadAsync<User>(id, ct);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Program.cs#L48-L82' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_UserEndpoints' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip INFO
The complete ASP<span/>.NET Core sample project is [available in the Marten codebase](https://github.com/JasperFx/marten/tree/master/src/AspNetCoreWithMarten)
:::

For more information on the query support within Marten, check [document querying](/documents/querying/)

There is a lot more capabilities than what we're showing here, so head on over to the table of contents on the sidebar to see what else Marten offers.

## Working with Events

Please check [Event Store quick start](/events/quickstart.md). Apart from the quick start, we also have an [EventStore intro](https://github.com/JasperFx/marten/blob/master/src/samples/EventSourcingIntro) .NET 6 sample project in the GitHub repository for your ready reference.

## Creating a standalone store

In some scenarios you may wish to create a document store outside of the generic host infrastructure. The easiest way do this is to use `DocumentStore.For`, either configuring `StoreOptions` or passing a plain connection string.

<!-- snippet: sample_start_a_store -->
<a id='snippet-sample_start_a_store'></a>
```cs
var store = DocumentStore
    .For("host=localhost;database=marten_testing;password=mypassword;username=someuser");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L35-L38' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start_a_store' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Please also check our [tutorials](/tutorials/) which introduces you to Marten through a real-world use case of building a freight and delivery management system using documents and event sourcing.
