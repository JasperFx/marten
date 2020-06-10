using System;
using System.IO;
using Oakton;
using Oakton.AspNetCore;

[assembly: OaktonCommandAssembly]

namespace Marten.CommandLine
{


    public class MartenInput : NetCoreInput
    {
        [Description("Option to store all output into a log file")]
        public string LogFlag { get; set; }

        private readonly StringWriter _log = new StringWriter();

        public void WriteLine(string text)
        {
            _log.WriteLine(text);
        }

        public void WriteLine(ConsoleColor color, string text)
        {
            ConsoleWriter.Write(color, text);
            _log.WriteLine(text);
        }

        public void WriteLogFileIfRequested()
        {
            if (LogFlag.IsEmpty())
                return;

            using (var stream = new FileStream(LogFlag, FileMode.Create))
            {
                var writer = new StreamWriter(stream);
                writer.WriteLine(_log.ToString());

                writer.Flush();
            }

            ConsoleWriter.Write(ConsoleColor.Green, "Wrote a log file to " + LogFlag);
        }
    }
}
