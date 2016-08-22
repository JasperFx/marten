using Oakton;

namespace Marten.CommandLine
{
    public class DumpInput : MartenInput
    {
        [Description("File (or folder) location to write the DDL file")]
        public string FileName { get; set; }

        [Description("Opt into writing the DDL split out by file")]
        [FlagAlias("by-type")]
        public bool ByTypeFlag { get; set; }
    }
}