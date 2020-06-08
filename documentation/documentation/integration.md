<!--title:Integrating Marten into .Net Applications-->

<[info]>
The built in DI service registration helpers were introduced in Marten v3.12.
<[/info]>

If your application uses an [IoC container](https://en.wikipedia.org/wiki/Inversion_of_control), 
the easiest way to integrate Marten into a .Net application is to add the key Marten services to
the underlying IoC container for the application.

As briefly shown in the <[linkto:getting_started]> page, Marten comes with extension methods
for the .Net Core standard `IServiceCollection` to quickly add Marten services to any .Net Core application that uses 
either the [Generic IHostBuilder abstraction](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-3.1) or the slightly older [ASP.Net Core IWebHostBuilder](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hosting.iwebhostbuilder?view=aspnetcore-3.1)
abstractions for bootstrapping applications.

Jumping right into a basic ASP.Net MVC Core application using the out of the box Web API template, you'd have a class called `Startup` that holds most of the configuration for your application including
the IoC service registrations for your application in the `ConfigureServices()` method. To add Marten
to your application, use the `AddMarten()` method as shown below:

<[sample:StartupConfigureServices]>

The `AddMarten()` method will...

MORE, WRITE MORE..........
