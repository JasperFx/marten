using Oakton;

namespace Marten.CommandLine.Commands.Patch
{
    public class PatchInput : MartenInput
    {
        [Description("File (or folder) location to write the DDL file")]
        public string FileName { get; set; }

        [Description("Opt into also writing out any missing schema creation scripts")]
        public bool SchemaFlag { get; set; }


        [Description("Override the location of the drop file")]
        public string DropFlag { get; set; }

        [Description("Option to create scripts as transactional script")]
        [FlagAlias("transactional-script")]
        public bool TransactionalScriptFlag { get; set; }
    }
}