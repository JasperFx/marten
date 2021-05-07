using System;
using System.Threading.Tasks;
using Baseline;
using Marten.Exceptions;
using Oakton;
using Weasel.Postgresql;

namespace Marten.CommandLine.Commands.Patch
{
    [Description(
         "Evaluates the current configuration against the database and writes a patch and drop file if there are any differences", Name = "marten-patch"
     )]
    public class PatchCommand: MartenCommand<PatchInput>
    {
        public PatchCommand()
        {
            Usage("Write the patch and matching drop file").Arguments(x => x.FileName);
        }

        protected override async Task<bool> execute(IDocumentStore store, PatchInput input)
        {
            try
            {
                await store.Schema.AssertDatabaseMatchesConfiguration();

                input.WriteLine(ConsoleColor.Green, "No differences were detected between the Marten configuration and the database");

                return true;
            }
            catch (SchemaValidationException)
            {
                var patch = await store.Schema.CreateMigration();

                input.WriteLine(ConsoleColor.Green, "Wrote a patch file to " + input.FileName);

                var rules = store.Options.As<StoreOptions>().Advanced.DdlRules;
                rules.IsTransactional = input.TransactionalScriptFlag;

                rules.WriteTemplatedFile(input.FileName, (r, w) => patch.WriteAllUpdates(w, r, AutoCreate.CreateOrUpdate));

                var dropFile = input.DropFlag ?? SchemaMigration.ToDropFileName(input.FileName);

                input.WriteLine(ConsoleColor.Green, "Wrote the drop file to " + dropFile);

                rules.WriteTemplatedFile(input.FileName, (r, w) => patch.WriteAllRollbacks(w, r));

                return true;
            }
        }
    }
}
