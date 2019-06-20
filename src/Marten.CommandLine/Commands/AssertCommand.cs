using System;
using Marten.Schema;
using Oakton;

namespace Marten.CommandLine.Commands
{
    [Description("Assert that the existing database matches the current Marten configuration")]
    public class AssertCommand: MartenCommand<MartenInput>
    {
        protected override bool execute(IDocumentStore store, MartenInput input)
        {
            try
            {
                store.Schema.AssertDatabaseMatchesConfiguration();

                input.WriteLine(ConsoleColor.Green, "No database differences detected.");

                return true;
            }
            catch (SchemaValidationException e)
            {
                input.WriteLine(ConsoleColor.Red, "The database does not match the configuration!");
                input.WriteLine(ConsoleColor.Yellow, e.ToString());

                input.WriteLine("The changes are the patch describing the difference between the database and the current configuration");

                return false;
            }
            catch (Exception e)
            {
                input.WriteLine(ConsoleColor.Red, "The database does not match the configuration!");
                input.WriteLine(ConsoleColor.Yellow, e.ToString());

                return false;
            }
        }
    }
}
