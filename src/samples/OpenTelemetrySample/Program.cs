using Marten;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetrySample;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services
    .AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services
    .AddEndpointsApiExplorer();
builder.Services
    .AddSwaggerGen();

builder.Services.AddMarten(options =>
{
    options.Connection("Host=localhost;Port=5432;Database=opentelemetry;Username=postgres;password=postgres;");

    options.Projections.SelfAggregate<Booking>(ProjectionLifecycle.Async);
    options.Projections.SelfAggregate<Payment>(ProjectionLifecycle.Async);
    options.Events.MetadataConfig.CausationIdEnabled = true;
    options.Events.MetadataConfig.CorrelationIdEnabled = true;
}).UseLightweightSessions()
.AddAsyncDaemon(DaemonMode.Solo);

builder.Services
    .AddOpenTelemetryTracing(builder =>
    {
        builder
        .AddSource("Sample")
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Sample"))
       .AddAspNetCoreInstrumentation()
       .AddMartenInstrumentation()
       .AddJaegerExporter()
       .SetSampler(new AlwaysOnSampler());
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
