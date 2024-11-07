using JasperFx.CommandLine;

namespace EventAppenderPerfTester;

public class ReadCommand: JasperFxAsyncCommand<NetCoreInput>
{
    public override async Task<bool> Execute(NetCoreInput input)
    {
        using var host = input.BuildHost();
        var trips = TripStreamReaderWriter.Read();

        Console.WriteLine("Read trips");

        return true;
    }
}
