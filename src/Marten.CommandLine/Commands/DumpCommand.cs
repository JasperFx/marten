using Oakton;

namespace Marten.CommandLine.Commands
{
    [Description("Dumps the entire DDL for the configured Marten database", Name = "marten-dump")]
    public class DumpCommand: Weasel.CommandLine.DumpCommand
    {
        public DumpCommand()
        {
            Usage("Writes the complete DDL for the entire Marten configuration to the named file")
                .Arguments(x => x.Path);
        }
    }
}
