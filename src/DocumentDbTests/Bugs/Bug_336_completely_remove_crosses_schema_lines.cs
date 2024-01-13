using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_336_completely_remove_crosses_schema_lines : BugIntegrationContext
{
    [Fact]
    public async Task do_not_remove_items_out_of_the_main_schema()
    {
        var store1 = TheStore;
        await store1.BulkInsertAsync(Target.GenerateRandomData(5).ToArray());
        await store1.BulkInsertAsync(new[] { new User() });
        var database1 = store1.Tenancy.Default.Database;
        (await database1.DocumentTables()).Any().ShouldBeTrue();

        var store2 = SeparateStore(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.All;
            _.DatabaseSchemaName = "other_bug";
        });

        await store2.BulkInsertAsync(Target.GenerateRandomData(5).ToArray());
        await store2.BulkInsertAsync(new[] { new User() });
        var database2 = store2.Tenancy.Default.Database;
        (await database2.DocumentTables()).Any().ShouldBeTrue();

        await store1.Advanced.Clean.CompletelyRemoveAllAsync();
        (await database1.DocumentTables()).Any().ShouldBeFalse();
        (await database2.DocumentTables()).Any().ShouldBeTrue();
    }
}