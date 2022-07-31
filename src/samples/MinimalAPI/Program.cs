using Marten;
using Marten.AsyncDaemon.Testing;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Projections;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Oakton;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMarten(opts =>
{
    opts.Connection(ConnectionSource.ConnectionString);
    opts.RegisterDocumentType<User>();
    opts.DatabaseSchemaName = "cli";

    // Register all event store projections ahead of time
    opts.Projections.Add(new TripAggregationWithCustomName(), ProjectionLifecycle.Async);
    opts.Projections.Add(new DayProjection(), ProjectionLifecycle.Async);
    opts.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

return await app.RunOaktonCommands(args);
