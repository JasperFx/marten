# Getting Started

Following the common .NET idiom, Marten supplies extension methods to quickly integrate Marten into any .NET application that uses the `IServiceCollection` abstractions to register IoC services. 
For this reason, we recommend starting with a Worker Service template for console applications, or an ASP.NET Core template for web services.

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

In the startup of your ASP&#46;NET Core or Worker Service application, make a call to `AddMarten()` to register Marten services like so:

<!-- snippet: sample_StartupConfigureServices -->
<a id='snippet-sample_startupconfigureservices'></a>
```cs
// This is the absolute, simplest way to integrate Marten into your
// .NET application with Marten's default configuration
builder.Services.AddMarten(options =>
{
    // Establish the connection string to your Marten database
    options.Connection(builder.Configuration.GetConnectionString("Marten")!);

    // Specify that we want to use STJ as our serializer
    options.UseSystemTextJsonForSerialization();

    // If we're running in development mode, let Marten just take care
    // of all necessary schema building and patching behind the scenes
    if (builder.Environment.IsDevelopment())
    {
        options.AutoCreateSchemaObjects = AutoCreate.All;
    }
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Program.cs#L16-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_startupconfigureservices' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

See [Bootstrapping with HostBuilder](/configuration/hostbuilder) for more information and options about this integration.

::: tip INFO
Use of the `.AddMarten` integration is not mandatory, see [Creating a standalone store](#creating-a-standalone-store)
:::

## Postgres

The next step is to get access to a PostgreSQL **12+** database schema. If you want to let Marten build database schema objects on the fly at development time,
make sure that your user account has rights to execute `CREATE TABLE/FUNCTION` statements.

Marten uses the [Npgsql](http://www.npgsql.org) library to access PostgreSQL from .NET, so you'll likely want to read their [documentation on connection string syntax](http://www.npgsql.org/doc/connection-string-parameters.html).

## Working with Documents

Now, for your first document type, we'll represent the users in our system:

<!-- snippet: sample_GettingStartedUser -->
<a id='snippet-sample_gettingstarteduser'></a>
```cs
public class User
{
    public Guid Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }

    public bool Internal { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/User.cs#L3-L12' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_gettingstarteduser' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

*For more information on document identity, see [identity](/documents/identity).*

From here, an instance of `IDocumentStore` or a type of `IDocumentSession` can be injected into the class/controller/endpoint of your choice and we can start persisting and loading user documents:

<!-- snippet: sample_UserEndpoints -->
<a id='snippet-sample_userendpoints'></a>
```cs
// You can inject the IDocumentStore and open sessions yourself
app.MapPost("/user",
    async (CreateUserRequest create, [FromServices] IDocumentStore store) =>
{
    // Open a session for querying, loading, and updating documents
    await using var session = store.LightweightSession();

    var user = new User {
        FirstName = create.FirstName,
        LastName = create.LastName,
        Internal = create.Internal
    };
    session.Store(user);

    await session.SaveChangesAsync();
});

app.MapGet("/users",
    async (bool internalOnly, [FromServices] IDocumentStore store, CancellationToken ct) =>
{
    // Open a session for querying documents only
    await using var session = store.QuerySession();

    return await session.Query<User>()
        .Where(x=> x.Internal == internalOnly)
        .ToListAsync(ct);
});

// OR Inject the session directly to skip the management of the session lifetime
app.MapGet("/user/{id:guid}",
    async (Guid id, [FromServices] IQuerySession session, CancellationToken ct) =>
{
    return await session.LoadAsync<User>(id, ct);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Program.cs#L45-L80' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_userendpoints' title='Start of snippet'>anchor</a></sup>
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
