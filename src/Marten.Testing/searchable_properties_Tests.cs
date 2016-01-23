using Marten.Schema;
using Marten.Services;
using Marten.Testing.Fixtures;
using System.Linq;
using Xunit;

namespace Marten.Testing
{
    public class searchable_properties_Tests : DocumentSessionFixture<IdentityMap>
    {
        [Fact]
        public void load_with_small_batch_and_duplicated_fields()
        {
            theContainer.GetInstance<IDocumentSchema>().Alter(_ =>
            {
                _.For<Target>().Searchable(x => x.String)
                    .Searchable(x => x.AnotherString);
            });

            var data = Target.GenerateRandomData(1).First();

            using (var session = CreateSession())
            {
                session.Store(data);
                session.SaveChanges();
            }
        }
    }
}