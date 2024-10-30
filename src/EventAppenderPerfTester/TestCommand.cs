using System.Diagnostics;
using JasperFx.CommandLine;
using Marten;
using Spectre.Console;

namespace EventAppenderPerfTester;

public class TestCommand: JasperFxAsyncCommand<TestInput>
{
    public override async Task<bool> Execute(TestInput input)
    {
        using var host = input.BuildHost();
        var store = host.DocumentStore();

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var testType = input.TypeFlag;

        if (testType == TestType.All)
        {
            var dict = new Dictionary<TestType, long>();
            dict[TestType.SingleFileSimple] = await executeTest(TestType.SingleFileSimple, store);
            dict[TestType.SingleFileFetchForWriting] = await executeTest(TestType.SingleFileFetchForWriting, store);
            dict[TestType.Multiples] = await executeTest(TestType.Multiples, store);

            var table = new Table();

            table.AddColumn("Test Name");
            table.AddColumn(new TableColumn("Duration in milliseconds").RightAligned());

            foreach (var pair in dict)
            {
                table.AddRow(pair.Key.ToString(), pair.Value.ToString());
            }

            AnsiConsole.Write(table);
        }
        else
        {
            await executeTest(testType, store);
        }

        return true;
    }

    private async Task<long> executeTest(TestType testType, IDocumentStore store)
    {
        AnsiConsole.MarkupLine($"[blue]Starting test {testType}[/]");
        var plan = buildPlan(testType);
        plan.FetchData();

        await using var session = store.LightweightSession();

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        await plan.Execute(session);

        stopwatch.Stop();

        var totalEvents = await store.Advanced.FetchEventStoreStatistics();
        AnsiConsole.MarkupLine($"[green]{testType} finished in {stopwatch.ElapsedMilliseconds} ms[/]");
        AnsiConsole.MarkupLine($"[blue]{totalEvents.EventCount} events in {totalEvents.StreamCount} streams[/]");


        return stopwatch.ElapsedMilliseconds;
    }

    private ITestPlan buildPlan(TestType inputType)
    {
        switch (inputType)
        {
            case TestType.SingleFileSimple:
                return new SingleFileSimplePlan();

            case TestType.SingleFileFetchForWriting:
                return new SingleFileFetchForWritingPlan();

            case TestType.Multiples:
                return new MultiplesTestPlan();
        }

        throw new ArgumentOutOfRangeException(nameof(inputType));
    }
}
