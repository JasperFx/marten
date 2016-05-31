using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Transforms;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class select_with_transformation : IntegratedFixture
    {
        public select_with_transformation()
        {
            StoreOptions(_ => _.Transforms.LoadFile("get_fullname.js"));
        }

        /* More Tests
         * 
         * 1. TransformToJson in compiled query
         * 2. TransformTo<T> sync
         * 3. TransformTo<T> async
         * 4. TransformToJson and TransformTo<T> in batch query
         * 5. TransformTo<T> in compiled query
         * 
         * 
         */

        [Fact]
        public void can_transform_to_json()
        {
            var user = new User {FirstName = "Eric", LastName = "Berry"};

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.SaveChanges();

                var json = session.Query<User>()
                    .Where(x => x.Id == user.Id)
                    .TransformToJson("get_fullname").Single();

                json.ShouldBe("{\"fullname\": \"Eric Berry\"}");
            }
        }

        [Fact]
        public async Task can_transform_to_json_async()
        {
            var user = new User { FirstName = "Eric", LastName = "Berry" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                await session.SaveChangesAsync();

                var json = await session.Query<User>()
                    .Where(x => x.Id == user.Id)
                    .TransformToJson("get_fullname").SingleAsync();

                json.ShouldBe("{\"fullname\": \"Eric Berry\"}");
            }
        }
    }
}