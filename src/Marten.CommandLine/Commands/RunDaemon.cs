using System;
using Marten.Events.Projections.Async;
using Oakton;

namespace Marten.CommandLine.Commands
{
    [Description("Run the async projections daemon")]
    public class RunDaemon: MartenCommand<MartenInput>
    {
        protected override bool execute(IDocumentStore store, MartenInput input)
        {
            var daemon = store.BuildProjectionDaemon(logger: new ConsoleDaemonLogger());
            daemon.StartAll();
            input.WriteLine(ConsoleColor.Green, "Daemon started. Press enter to stop.");
            Console.ReadLine();
            daemon.StopAll();
            input.WriteLine(ConsoleColor.DarkCyan, "Daemon stopped");
            return true;
        }
    }
}
