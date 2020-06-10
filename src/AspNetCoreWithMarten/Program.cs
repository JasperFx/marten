using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oakton.AspNetCore;

namespace AspNetCoreWithMarten
{
    // SAMPLE: SampleConsoleApp
    public class Program
    {
        // It's actually important to return Task<int>
        // so that the application commands can communicate
        // success or failure
        public static Task<int> Main(string[] args)
        {
            return CreateHostBuilder(args)

                // This line replaces Build().Start()
                // in most dotnet new templates
                .RunOaktonCommands(args);
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
    // ENDSAMPLE
}
