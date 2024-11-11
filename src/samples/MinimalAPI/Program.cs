using Marten;
using DaemonTests;
using DaemonTests.TestingSupport;
using JasperFx;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
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

// Instead of App.Run(), use this syntax
// as the last line of your Program.cs file
// to get extended command line options
return await app.RunJasperFxCommands(args);

#endregion
