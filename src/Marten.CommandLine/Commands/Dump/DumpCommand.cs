using System;
using System.IO;
using System.Threading.Tasks;
using Baseline;
using Oakton;

namespace Marten.CommandLine.Commands.Dump
{
    [Description("Dumps the entire DDL for the configured Marten database", Name = "marten-dump")]
    public class DumpCommand: MartenCommand<DumpInput>
    {
        public DumpCommand()
        {
            Usage("Writes the complete DDL for the entire Marten configuration to the named file")
                .Arguments(x => x.FileName);
        }

        protected override Task<bool> execute(IDocumentStore store, DumpInput input)
        {
            if (input.ByTypeFlag)
            {
                input.WriteLine("Writing DDL files to " + input.FileName);
                store.Schema.WriteDatabaseCreationScriptByType(input.FileName);

                // You only need to clean out the existing folder when dumping
                // by type
                try
                {
                    if (Directory.Exists(input.FileName))
                    {
                        new FileSystem().CleanDirectory(input.FileName);
                    }
                }
                catch (Exception)
                {
                    input.WriteLine(ConsoleColor.Yellow, $"Unable to clean the directory at {input.FileName} before writing new files");
                }
            }
            else
            {
                input.WriteLine("Writing DDL file to " + input.FileName);



                store.Schema.WriteDatabaseCreationScriptFile(input.FileName);
            }

            return Task.FromResult(true);
        }
    }
}
