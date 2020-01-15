using System;
using Oakton;

namespace Marten.CommandLine.Commands
{
    [Description("Applies all outstanding changes to the database based on the current configuration")]
    public class ApplyCommand: MartenCommand<MartenInput>
    {
        protected override bool execute(IDocumentStore store, MartenInput input)
        {
            try
            {
                store.Schema.ApplyAllConfiguredChangesToDatabase();

                input.WriteLine(ConsoleColor.Green, "Successfully applied outstanding database changes");
                return true;
            }
            catch (Exception e)
            {
                input.WriteLine(ConsoleColor.Red, "Failed to apply outstanding database changes!");
                input.WriteLine(ConsoleColor.Yellow, e.ToString());

                throw;
            }
        }
    }
}
