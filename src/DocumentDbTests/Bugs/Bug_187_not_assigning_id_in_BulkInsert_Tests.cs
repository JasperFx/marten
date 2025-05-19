using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_187_not_assigning_id_in_BulkInsert_Tests: IntegrationContext
{
    [Fact]
    public async Task does_indeed_assign_the_id_during_bulk_insert()
    {
        var docs = new IntDoc[50];
        for (var i = 0; i < docs.Length; i++)
        {
            docs[i] = new IntDoc();
        }

        await theStore.BulkInsertAsync(docs);

        using (var session = theStore.QuerySession())
        {
            session.Query<IntDoc>().Count().ShouldBe(50);
        }
    }

    public Bug_187_not_assigning_id_in_BulkInsert_Tests(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
