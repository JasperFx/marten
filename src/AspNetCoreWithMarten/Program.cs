using AspNetCoreWithMarten;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Oakton;
using Weasel.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Host.ApplyOaktonExtensions();

#region sample_StartupConfigureServices
// This is the absolute, simplest way to integrate Marten into your
// .NET application with Marten's default configuration
builder.Services.AddMarten(options =>
{
    // Establish the connection string to your Marten database
    options.Connection(builder.Configuration.GetConnectionString("Marten")!);

    // If we're running in development mode, let Marten just take care
    // of all necessary schema building and patching behind the scenes
    if (builder.Environment.IsDevelopment())
    {
        options.AutoCreateSchemaObjects = AutoCreate.All;
    }
});
#endregion

var app = builder.Build();


app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

#region sample_UserEndpoints
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
#endregion


await app.RunOaktonCommands(args);


record CreateUserRequest(string FirstName, string LastName, bool Internal);

