using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Reading.Linq
{
    public class query_using_contains : IntegrationContext
    {
        [Fact]
        public async Task get_distinct_number()
        {
            theStore.Options.Schema.For<Target>()
                .Duplicate(x => x.UserIds);

            theSession.Store(new Target {Id = 1, IsPublic = true, UserIds = new [] { 1, 2, 3, 4, 5, 6 }});
            theSession.Store(new Target {Id = 2, IsPublic = false, UserIds = new int[] { }});
            theSession.Store(new Target {Id = 3, IsPublic = true, UserIds = new [] { 1, 2, 3 }});
            theSession.Store(new Target {Id = 4, IsPublic = true, UserIds = new [] { 1, 2, 6 }});
            theSession.Store(new Target {Id = 5, IsPublic = true, UserIds = new [] { 4, 5, 6 }});
            theSession.Store(new Target {Id = 6, IsPublic = true, UserIds = new [] { 6 }});

            await theSession.SaveChangesAsync();

            using (var sess = theStore.LightweightSession())
            {
                // This currently fails due to the way the query uses unnest to assume the array has items
                // since the array is empty the unnest results in 0 records to query against
                var result1 = theSession.Query<Target>().Where(x => x.IsPublic == false || x.UserIds.Contains(10)).ToList();

                result1.ShouldContain(x => x.Id == 2);

                // This should pass without any error as the query will return results
                var result2 = await theSession.Query<Target>().Where(x => x.IsPublic || x.UserIds.Contains(5)).ToListAsync();

                result2.ShouldContain(x => x.Id == 1);
                result2.ShouldContain(x => x.Id == 5);
            }
        }

        public query_using_contains(DefaultStoreFixture fixture) : base(fixture)
        {
        }

        public class Target
        {
            public int Id { get; set; }

            public bool IsPublic { get; set; }

            public int[] UserIds { get; set; }
        }
    }
}
