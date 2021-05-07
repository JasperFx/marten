using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_336_completely_remove_crosses_schema_lines : BugIntegrationContext
    {
        [Fact]
        public async Task do_not_remove_items_out_of_the_main_schema()
        {
            var store1 = theStore;
            await store1.BulkInsertAsync(Target.GenerateRandomData(5).ToArray());
            await store1.BulkInsertAsync(new[] { new User() });
            (await store1.Tenancy.Default.DocumentTables()).Any().ShouldBeTrue();

            var store2 = SeparateStore(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.DatabaseSchemaName = "other_bug";
            });

            await store2.BulkInsertAsync(Target.GenerateRandomData(5).ToArray());
            await store2.BulkInsertAsync(new[] { new User() });
            (await store2.Tenancy.Default.DocumentTables()).Any().ShouldBeTrue();

            store1.Advanced.Clean.CompletelyRemoveAll();
            (await store1.Tenancy.Default.DocumentTables()).Any().ShouldBeFalse();
            (await store2.Tenancy.Default.DocumentTables()).Any().ShouldBeTrue();
        }
    }
}
