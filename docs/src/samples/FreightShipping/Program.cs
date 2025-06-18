// See https://aka.ms/new-console-template for more information

using FreightShipping;
using FreightShipping.EventSourcedAggregate;

if (args.Length == 0)
{
    await ShowUsage();
    return;
}

var command = args[0].ToLowerInvariant();
using var cts = new CancellationTokenSource();

// Ctrl + C
Console.CancelKeyPress += (sender, e) =>
{
    Console.WriteLine("Ctrl+C pressed. Shutting down...");
    e.Cancel = true; // prevent immediate termination
    cts.Cancel();
};

var task = command switch
{
    "getting-started"    => RunAndWait(GettingStarted.Run, true),
    "modeling-documents" => RunAndWait(ModelingDocuments.Run, true),
    "evolve-to-eventsourcing" => RunAndWait(EvolveToEventSourcing.Run, true),
    "eventsourced-aggregate" => RunAndWait(EventSourcedAggregate.Run, true),
    "cross-aggregate-views-async-daemon" => _ = CrossAggregateViews.RunDaemon(cts.Token),
    "cross-aggregate-views" => RunAndWait(CrossAggregateViews.Run, true),
    "wolverine-integration" => CrossAggregateViews.RunDaemon(cts.Token),
    _ => ShowUsage()
};

await task;
return;

static async Task ShowUsage()
{
    await Console.Out.WriteLineAsync("Valid commands are:\n" +
                                     "getting-started\n" +
                                     "modeling-documents.\n" +
                                     "evolve-to-eventsourcing\n" +
                                     "eventsourced-aggregate\n" +
                                     "cross-aggregate-views-async-daemon\n" +
                                     "cross-aggregate-views\n" +
                                     "wolverine-integration");
}

static async Task RunAndWait(Func<Task> handler, bool waitForKey = false)
{
    await handler();
    if (waitForKey)
    {
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey(true);
    }
}