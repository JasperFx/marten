using System.Linq;
using Marten.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    [SelectionStoryteller]
    public class Bug_id_field_does_not_hit_id_column: IntegrationContextWithIdentityMap<NulloIdentityMap>
    {
        [Fact]
        public void return_the_correct_number_of_results()
        {
            var target = new Target
            {
                Id = System.Guid.NewGuid()
            };

            theStore.BulkInsert(new[] { target });

            var queryable = theSession.Query<Target>()
                .Where(x => x.Id == target.Id);

            var cmd = queryable.ToCommand(FetchType.FetchMany);

            SpecificationExtensions.ShouldContain(cmd.CommandText, "where d.id = :arg0");

            queryable.ToArray().Length.ShouldBe(1);
        }

        public Bug_id_field_does_not_hit_id_column(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
