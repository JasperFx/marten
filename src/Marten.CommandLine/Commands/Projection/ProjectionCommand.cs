using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oakton;
using Spectre.Console;

namespace Marten.CommandLine.Commands.Projection
{
    [Description("Rebuilds all projections of specified kind")]
    public class ProjectionsCommand: OaktonAsyncCommand<ProjectionInput>
    {
        private TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>();

        public override async Task<bool> Execute(ProjectionInput input)
        {
            input.HostBuilder.ConfigureLogging(x => x.ClearProviders());

            using var host = input.BuildHost();
            var store = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();

            if (input.ListFlag)
            {
                WriteProjectionTable(store);
                return true;
            }



            var assembly = Assembly.GetEntryAssembly();
            AssemblyLoadContext.GetLoadContext(assembly).Unloading += context => Shutdown();

            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                Shutdown();
                eventArgs.Cancel = true;
            };

            var shutdownMessage = "Press CTRL + C to quit";
            Console.WriteLine(shutdownMessage);


            var daemon = store.BuildProjectionDaemon();
            daemon.Tracker.Subscribe(new ProjectionWatcher(_completion.Task));
            await daemon.StartAll();


            await _completion.Task;

            return true;
        }

        public void Shutdown()
        {
            _completion.TrySetResult(true);
        }

        private static void WriteProjectionTable(DocumentStore store)
        {
            var projections = store.Options.Events.Projections.Projections;

            var table = new Table();
            table.AddColumn("Projection Name");
            table.AddColumn("Class");
            table.AddColumn("Shards");
            table.AddColumn("Lifecycle");

            foreach (var projection in projections)
            {
                var shards = projection.AsyncProjectionShards(store).Select(x => x.Name.Identity).Join(", ");
                table.AddRow(projection.ProjectionName, projection.GetType().FullNameInCode(), shards, projection.Lifecycle.ToString());
            }

            AnsiConsole.Render(table);
        }
    }
}
