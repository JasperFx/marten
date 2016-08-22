using System;
using System.IO;
using Baseline;
using Oakton;

namespace Marten.CommandLine
{
    public class MartenInput
    {
        [Description("Use to override the Postgresql connection string")]
        public string ConnFlag { get; set; }

        [Description("Option to store all output into a log file")]
        public string LogFlag { get; set; }

        internal StoreOptions Options { get; set; }

        internal IDocumentStore CreateStore()
        {
            if (ConnFlag.IsNotEmpty())
            {
                Options.Connection(ConnFlag);
            }

            return new DocumentStore(Options);
        }

        private readonly StringWriter _log = new StringWriter();

        public void WriteLine(string text)
        {
            Console.WriteLine(text);
            _log.WriteLine(text);
        }

        public void WriteLine(ConsoleColor color, string text)
        {
            ConsoleWriter.Write(color, text);
            _log.WriteLine(text);
        }

        public void WriteLogFileIfRequested()
        {
            if (LogFlag.IsEmpty()) return;

            

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