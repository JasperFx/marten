// See https://aka.ms/new-console-template for more information

using System.Collections.Specialized;
using FreightShipping;
using FreightShipping.EventSourcedAggregate;

using var cts = new CancellationTokenSource();

var commandMap = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
{
    ["getting-started"] = () => RunAndWait(GettingStarted.Run, true),
    ["modeling-documents"] = () => RunAndWait(ModelingDocuments.Run, true),
    ["evolve-to-event-sourcing"] = () => RunAndWait(EvolveToEventSourcing.Run, true),
    ["event-sourced-aggregate"] = () => RunAndWait(EventSourcedAggregate.Run, true),
    // ReSharper disable once AccessToDisposedClosure
    ["cross-aggregate-views-async-daemon"] = () => _ = CrossAggregateViews.RunDaemon(cts.Token),
    ["cross-aggregate-views"] = () => RunAndWait(CrossAggregateViews.Run, true),
    // ReSharper disable once AccessToDisposedClosure
    ["wolverine-integration"] = () => CrossAggregateViews.RunDaemon(cts.Token)
};

var commands = commandMap.Keys.Cast<string>().ToArray();

if (args.Length == 0)
{
    await ShowUsage(commands);
    return;
}

var command = args[0].ToLowerInvariant();

// Ctrl + C
Console.CancelKeyPress += (_, e) =>
{
    Console.WriteLine("Ctrl+C pressed. Shutting down...");
    e.Cancel = true; // prevent immediate termination
    // ReSharper disable once AccessToDisposedClosure
    cts.Cancel();
};

if (commandMap.Contains(command))
{
    await (((Func<Task>)commandMap[command]!))();
}
else
{
    await ShowUsage(commands);
}

return;

static async Task ShowUsage(string[] commands)
{
    await Console.Out.WriteLineAsync(string.Join("\n", new[] { "Available commands:" }.Concat(commands)));
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