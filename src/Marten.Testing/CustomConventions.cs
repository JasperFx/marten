using System;
using System.Linq;
using Fixie;
using Marten.Schema;

namespace Marten.Testing
{
    public class CustomConventions : Fixie.Convention
    {
        public CustomConventions()
        {
            Classes.NameEndsWith("Tests");
            Methods.Where(x => x.IsVoid());

            if (Options.Contains("upsert"))
            {
                DevelopmentModeRegistry.UpsertType = (PostgresUpsertType) Enum.Parse(typeof(PostgresUpsertType), Options["upsert"].Single());
            }
            else
            {
                DevelopmentModeRegistry.UpsertType = PostgresUpsertType.Legacy;
            }

            Console.WriteLine("The UpsertStyle is " + DevelopmentModeRegistry.UpsertType);
        }
    }
}