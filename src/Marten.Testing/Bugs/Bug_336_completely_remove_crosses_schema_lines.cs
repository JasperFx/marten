using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_336_completely_remove_crosses_schema_lines : BugIntegrationContext
    {
        [Fact]
        public void do_not_remove_items_out_of_the_main_schema()
        {
            var store1 = theStore;
            store1.BulkInsert(Target.GenerateRandomData(5).ToArray());
            store1.BulkInsert(new[] { new User() });
            store1.Tenancy.Default.DbObjects.DocumentTables().Any().ShouldBeTrue();

            var store2 = SeparateStore(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.DatabaseSchemaName = "other";
            });

            store2.BulkInsert(Target.GenerateRandomData(5).ToArray());
            store2.BulkInsert(new[] { new User() });
            store2.Tenancy.Default.DbObjects.DocumentTables().Any().ShouldBeTrue();

            store1.Advanced.Clean.CompletelyRemoveAll();
            store1.Tenancy.Default.DbObjects.DocumentTables().Any().ShouldBeFalse();
            store2.Tenancy.Default.DbObjects.DocumentTables().Any().ShouldBeTrue();
        }
    }
}
