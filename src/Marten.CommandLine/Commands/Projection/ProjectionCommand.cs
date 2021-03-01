using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
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
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

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


            if (input.RebuildFlag)
            {
                return await Rebuild(input, store);
            }

            return await RunContinuously(input, store);
        }

        private async Task<bool> Rebuild(ProjectionInput input, DocumentStore store)
        {
            var projections = input.SelectProjections(store);

            if (input.InteractiveFlag)
            {
                var projectionNames = store.Events.Projections.Projections.Select(x => x.ProjectionName).ToArray();
                var names = AnsiConsole.Prompt(new MultiSelectionPrompt<string>()
                    .Title("Choose projections to rebuild")
                    .AddChoices(projectionNames));

                projections = store
                    .Options
                    .Events
                    .Projections
                    .Projections
                    .Where(x => names.Contains(x.ProjectionName))
                    .ToList();
            }


            if (!projections.Any())
            {
                Console.WriteLine("No projections to rebuild.");
                return true;
            }

            var daemon = store.BuildProjectionDaemon();
            await daemon.StartDaemon();

            var highWater = daemon.Tracker.HighWaterMark;
            var watcher = new RebuildWatcher(highWater, _completion.Task);
            using var unsubscribe = daemon.Tracker.Subscribe(watcher);

            var tasks = projections
                .Select(x => Task.Run(async () => await daemon.RebuildProjection(x.ProjectionName, _cancellation.Token)))
                .ToArray();

            await Task.WhenAll(tasks);

            _completion.SetResult(true);

            Console.WriteLine("Projection Rebuild complete!");

            return true;

        }

        private async Task<bool> RunContinuously(ProjectionInput input, DocumentStore store)
        {
            var shards = input.BuildShards(store);
            if (!shards.Any())
            {
                Console.WriteLine(input.ProjectionFlag.IsEmpty()
                    ? "No projections are registered with an asynchronous life cycle."
                    : $"No projection or projection shards match the requested filter '{input.ProjectionFlag}'");

                Console.WriteLine();
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
            daemon.Tracker.Subscribe(new ProjectionWatcher(_completion.Task, shards));

            foreach (var shard in shards)
            {
                await daemon.StartShard(shard, _cancellation.Token);
            }


            await _completion.Task;
            return false;
        }

        public void Shutdown()
        {
            _cancellation.Cancel();
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
