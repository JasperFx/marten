using Marten;
using Microsoft.AspNetCore.OData;
using Microsoft.OData.ModelBuilder;
using System.ComponentModel.DataAnnotations;
using Marten.Testing.Harness;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(4000);
});

builder.Services.AddMarten((options) =>
{
    options.Connection(ConnectionSource.ConnectionString);
});

builder.Services.AddControllers().AddOData(options =>
{
    var entityBuilder = new ODataModelBuilder();

    entityBuilder.EntityType<WeatherForecast>().HasKey(t => t.Id);
    entityBuilder.EntitySet<WeatherForecast>("WeatherForecast");

    options.EnableQueryFeatures().AddRouteComponents("odata", entityBuilder.GetEdmModel());
});

var app = builder.Build();

app.MapControllers();

app.Run();

public record WeatherForecast()
{
    public Guid Id { get; init; }
    public DateOnly Date { get; init; }
    public int Temperature { get; init; }
    public string? Summary { get; init; }
    public decimal? Humidity { get; init; }
}
