using Oakton;

namespace EventAppenderPerfTester;

public class ReadCommand : OaktonAsyncCommand<NetCoreInput>
{
    public override async Task<bool> Execute(NetCoreInput input)
    {
        using var host = input.BuildHost();
        var trips = TripStreamReaderWriter.Read();

        Console.WriteLine("Read trips");

        return true;
    }
}