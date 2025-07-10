using AspNetCoreWithMarten;
using JasperFx;
using Marten;
using Marten.Services.Json;
using Microsoft.AspNetCore.Mvc;
using JasperFx;
using Weasel.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Host.ApplyJasperFxExtensions();

#region sample_StartupConfigureServices
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
#endregion

var app = builder.Build();


app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

#region sample_UserEndpoints
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
#endregion


return await app.RunJasperFxCommands(args);


record CreateUserRequest(string FirstName, string LastName, bool Internal);

