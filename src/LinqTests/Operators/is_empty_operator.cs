using System.Linq;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace LinqTests.Operators;

public class is_empty_operator : IntegrationContext
{
    [Fact]
    public void use_is_empty()
    {
        var doc1 = Target.Random(false);
        var doc2 = Target.Random(true);
        var doc3 = Target.Random(false);
        var doc4 = Target.Random(true);

        var empties = new[] {doc1, doc3}.OrderBy(x => x.Id).Select(x => x.Id).ToArray();

        using (var session = theStore.LightweightSession())
        {
            session.Store(doc1, doc2, doc3, doc4);
            session.SaveChanges();
        }

        using (var query = theStore.QuerySession())
        {
            query.Query<Target>().Where(x => x.Children.IsEmpty())
                .OrderBy(x => x.Id).Select(x => x.Id)
                .ToList()
                .ShouldHaveTheSameElementsAs(empties);


        }
    }

    public is_empty_operator(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
