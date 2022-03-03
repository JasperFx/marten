using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oakton;
using Spectre.Console;

namespace Marten.CommandLine.Commands.Projection
{
    [Description("Marten's asynchronous projection and projection rebuilds")]
    public class ProjectionsCommand: OaktonAsyncCommand<ProjectionInput>
    {
        private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _cancellation = new();

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
                return await Rebuild(input, store).ConfigureAwait(false);
            }

            return await RunContinuously(input, store).ConfigureAwait(false);
        }

        private async Task<bool> Rebuild(ProjectionInput input, DocumentStore store)
        {
            var projections = input.SelectProjections(store);

            if (input.InteractiveFlag)
            {
                var projectionNames = store.Options.Projections.All.Where(x => x.Lifecycle != ProjectionLifecycle.Live).Select(x => x.ProjectionName).ToArray();
                var names = AnsiConsole.Prompt(new MultiSelectionPrompt<string>()
                    .Title("Choose projections to rebuild")
                    .AddChoices(projectionNames));

                projections = store
                    .Options
                    .Projections
                    .All
                    .Where(x => names.Contains(x.ProjectionName))
                    .ToList();
            }


            if (!projections.Any())
            {
                Console.WriteLine("No projections to rebuild.");
                return true;
            }

            var daemon = await store.BuildProjectionDaemonAsync().ConfigureAwait(false);
            await daemon.StartDaemon().ConfigureAwait(false);

            var highWater = daemon.Tracker.HighWaterMark;
            var watcher = new RebuildWatcher(highWater, _completion.Task);
            using var unsubscribe = daemon.Tracker.Subscribe(watcher);
#if NET6_0_OR_GREATER
            await Parallel.ForEachAsync(projections, _cancellation.Token,
                    async (projection, token) =>
                        await daemon.RebuildProjection(projection.ProjectionName, token).ConfigureAwait(false))
                .ConfigureAwait(false);

#else
            var tasks = projections
                .Select(x => Task.Run(async () => await daemon.RebuildProjection(x.ProjectionName, _cancellation.Token).ConfigureAwait(false)))
                .ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);
#endif


            _completion.SetResult(true);

            Console.WriteLine("Projection Rebuild complete!");

            return true;

        }

        private async Task<bool> RunContinuously(ProjectionInput input, DocumentStore store)
        {
            var shards = input.BuildShards(store);

            if (input.InteractiveFlag)
            {
                var all = store.Options.Projections.All.Where(x => x.Lifecycle != ProjectionLifecycle.Live).SelectMany(x => x.AsyncProjectionShards(store))
                    .Select(x => x.Name.Identity).ToArray();

                var prompt = new MultiSelectionPrompt<string>()
                    .Title("Choose projection shards to run continuously")
                    .AddChoices(all);

                var selections = AnsiConsole.Prompt(prompt);
                shards = store.Options.Projections.All.SelectMany(x => x.AsyncProjectionShards(store))
                    .Where(x => selections.Contains(x.Name.Identity)).ToList();
            }


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


            var daemon = (ProjectionDaemon)await store.BuildProjectionDaemonAsync().ConfigureAwait(false);
            daemon.Tracker.Subscribe(new ProjectionWatcher(_completion.Task, shards));

            foreach (var shard in shards)
            {
                await daemon.StartShard(shard, _cancellation.Token).ConfigureAwait(false);
            }


            await _completion.Task.ConfigureAwait(false);
            return false;
        }

        public void Shutdown()
        {
            _cancellation.Cancel();
            _completion.TrySetResult(true);
        }

        private static void WriteProjectionTable(DocumentStore store)
        {
            var projections = store.Options.Projections.All;

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
