using System.Linq;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_336_completely_remove_crosses_schema_lines
    {
        [Fact]
        public void do_not_remove_items_out_of_the_main_schema()
        {
            var store1 = TestingDocumentStore.DefaultSchema();
            store1.BulkInsert(Target.GenerateRandomData(5).ToArray());
            store1.BulkInsert(new [] { new User()});
            store1.Schema.DbObjects.DocumentTables().Any().ShouldBeTrue();

            var store2 = TestingDocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "other";
            });

            store2.BulkInsert(Target.GenerateRandomData(5).ToArray());
            store2.BulkInsert(new[] { new User() });
            store2.Schema.DbObjects.DocumentTables().Any().ShouldBeTrue();



            store1.Advanced.Clean.CompletelyRemoveAll();
            store1.Schema.DbObjects.DocumentTables().Any().ShouldBeFalse();
            store2.Schema.DbObjects.DocumentTables().Any().ShouldBeTrue();
        }
    }
}