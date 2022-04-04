// See https://aka.ms/new-console-template for more information

using LamarCodeGeneration;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Oakton;
using SharedDataLayer;

return Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMarten(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.GeneratedCodeMode = TypeLoadMode.Auto;

            opts.RegisterDocumentType<Invoice>();
            opts.RegisterDocumentType<Order>();
        });

        services.SetApplicationProject(typeof(Order).Assembly);
    }).RunOaktonCommandsSynchronously(args);
