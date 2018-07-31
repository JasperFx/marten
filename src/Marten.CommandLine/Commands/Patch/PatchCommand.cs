using System;
using Marten.Schema;
using Oakton;

namespace Marten.CommandLine.Commands.Patch
{
    [Description(
         "Evaluates the current configuration against the database and writes a patch and drop file if there are any differences"
     )]
    public class PatchCommand : MartenCommand<PatchInput>
    {
        public PatchCommand()
        {
            Usage("Write the patch and matching drop file").Arguments(x => x.FileName);
        }

        protected override bool execute(IDocumentStore store, PatchInput input)
        {
            try
            {
                store.Schema.AssertDatabaseMatchesConfiguration();


                input.WriteLine(ConsoleColor.Green, "No differences were detected between the Marten configuration and the database");

                return true;
            }
            catch (SchemaValidationException)
            {
                var patch = store.Schema.ToPatch(input.SchemaFlag, withAutoCreateAll: true);

                input.WriteLine(ConsoleColor.Green, "Wrote a patch file to " + input.FileName);
                patch.WriteUpdateFile(input.FileName, input.TransactionalScriptFlag);


                var dropFile = input.DropFlag ?? SchemaPatch.ToDropFileName(input.FileName);

                input.WriteLine(ConsoleColor.Green, "Wrote the drop file to " + dropFile);
                patch.WriteRollbackFile(dropFile, input.TransactionalScriptFlag);

                return true;
            }



        }
    }
}