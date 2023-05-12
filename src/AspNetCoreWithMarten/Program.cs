using Marten;
using Oakton;
using Weasel.Core;


#region sample_SampleConsoleApp
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();


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

app.MapGet("/", context => context.Response.WriteAsync("Hello World!"));

await app.RunOaktonCommands(args);
#endregion
