using Oakton;

namespace Marten.CommandLine
{
    public class PatchInput : MartenInput
    {
        [Description("File (or folder) location to write the DDL file")]
        public string FileName { get; set; }

        [Description("Opt into also writing out any missing schema creation scripts")]
        public bool SchemaFlag { get; set; }
    }
}