using Baseline;
using Oakton;

namespace Marten.CommandLine
{
    public class PatchInput : MartenInput
    {
        [Description("File (or folder) location to write the DDL file")]
        public string FileName { get; set; }
    }

    [Description("Evaluates the current configuration against the database and writes a patch and drop file if there are any differences")]
    public class PatchCommand : MartenCommand<PatchInput>
    {
        public PatchCommand()
        {
            Usage("Write the patch and matching drop file").Arguments(x => x.FileName);
        }

        protected override void execute(IDocumentStore store, PatchInput input)
        {
            var patch = store.Schema.ToPatch(false);

            if (patch.UpdateDDL.IsNotEmpty())
            {

            }
            else
            {
                
            }
        }
    }


    
    



}