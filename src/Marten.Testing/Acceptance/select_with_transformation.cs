using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
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


        public class JsonQuery : ICompiledQuery<User, string>
        {
            public Expression<Func<IQueryable<User>, string>> QueryIs()
            {
                return _ => _.Where(x => x.FirstName == FirstName).TransformToJson("get_fullname").Single();
            }

            public string FirstName { get; set; }
        }

        [Fact]
        public void transform_to_json_in_compiled_query()
        {
            var user = new User { FirstName = "Eric", LastName = "Berry" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.SaveChanges();

                var json = session.Query(new JsonQuery {FirstName = "Eric"});

                json.ShouldBe("{\"fullname\": \"Eric Berry\"}");
            }
        }

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

        public class FullNameView
        {
            public string fullname { get; set; }
        }

        [Fact]
        public void can_transform_to_another_doc()
        {
            var user = new User { FirstName = "Eric", LastName = "Berry" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.SaveChanges();

                var view = session.Query<User>()
                    .Where(x => x.Id == user.Id)
                    .TransformTo<FullNameView>("get_fullname").Single();

                view.fullname.ShouldBe("Eric Berry");
            }
        }

        [Fact]
        public async Task can_transform_to_another_doc_async()
        {
            var user = new User { FirstName = "Eric", LastName = "Berry" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                await session.SaveChangesAsync();

                var view = await session.Query<User>()
                    .Where(x => x.Id == user.Id)
                    .TransformTo<FullNameView>("get_fullname").SingleAsync();

                view.fullname.ShouldBe("Eric Berry");
            }
        }

        public class FullNameViewQuery : ICompiledQuery<User, FullNameView>
        {
            public Expression<Func<IQueryable<User>, FullNameView>> QueryIs()
            {
                return _ => _.Where(x => x.FirstName == FirstName).TransformTo<FullNameView>("get_fullname").Single();
            }

            public string FirstName { get; set; }
        }

        [Fact]
        public void transform_to_other_type_in_compiled_query()
        {
            var user = new User { FirstName = "Eric", LastName = "Berry" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.SaveChanges();

                var view = session.Query(new FullNameViewQuery() { FirstName = "Eric" });

                view.fullname.ShouldBe("Eric Berry");
            }
        }

    }
}