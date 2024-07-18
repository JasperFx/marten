using System.Runtime.CompilerServices;
using DaemonTests.TestingSupport;
using Oakton;
using Xunit.Abstractions;

namespace EventAppenderPerfTester;

public class ExportCommand : OaktonAsyncCommand<NetCoreInput>
{
    public override async Task<bool> Execute(NetCoreInput input)
    {
        using var host = input.BuildHost();
        var trips = new List<TripStream>();
        for (int i = 0; i < 100; i++)
        {
            trips.Add(new TripStream());
        }

        await TripStreamReaderWriter.Write(trips.ToArray());

        Console.WriteLine("Wrote 100 trip streams to " + TripStreamReaderWriter.Path);

        return true;
    }
}
