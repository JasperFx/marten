using DaemonTests.Aggregations;
using DaemonTests.EventProjections;
using DaemonTests.TestingSupport;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

#region sample_using_WebApplication_1

var builder = WebApplication.CreateBuilder(args);

// Easiest to just do this right after creating builder
// Must be done before calling builder.Build() at least
builder.Host.ApplyJasperFxExtensions();

#endregion

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

    opts.Events.UseArchivedStreamPartitioning = true;

    opts.Schema.For<Target>().SoftDeletedWithPartitioningAndIndex();

    // Register all event store projections ahead of time
    opts.Projections.Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Async);
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

#region sample_using_WebApplication_2

// Instead of App.Run(), use the app.RunJasperFxCommands(args)
// as the last line of your Program.cs file
return await app.RunJasperFxCommands(args);

#endregion
