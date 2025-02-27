using System.IO;
using JasperFx.CommandLine;
using Spectre.Console;

[assembly: JasperFxAssembly]

namespace Marten.CommandLine;

public class MartenInput: NetCoreInput
{
    private readonly StringWriter _log = new();

    [Description("Option to store all output into a log file")]
    public string LogFlag { get; set; }

    public void WriteLine(string text)
    {
        _log.WriteLine(text);
    }


    public void WriteLogFileIfRequested()
    {
        if (LogFlag.IsEmpty())
        {
            return;
        }

        using (var stream = new FileStream(LogFlag, FileMode.Create))
        {
            var writer = new StreamWriter(stream);
            writer.WriteLine(_log.ToString());

            writer.Flush();
        }

        AnsiConsole.Write($"[green]Wrote a log file to {LogFlag}[/]");
    }
}
