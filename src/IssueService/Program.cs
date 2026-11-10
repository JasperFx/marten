using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace IssueService;

public class Program
{
    // WebApplicationBuilder in the entrypoint: ASP.NET Core 11's WebApplicationFactory
    // (and so AlbaHost.For<Program>) refuses to override ConfigureWebApplicationBuilder
    // for a generic-host entrypoint. Startup is still used, wired by hand.
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var startup = new Startup(builder.Configuration);
        startup.ConfigureServices(builder.Services);

        var app = builder.Build();
        startup.Configure(app, app.Environment);

        app.Run();
    }
}
