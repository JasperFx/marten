using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Transforms;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class select_with_transformation: IntegrationContext
    {
        public select_with_transformation(DefaultStoreFixture fixture) : base(fixture)
        {
            StoreOptions(_ => _.Transforms.LoadFile("get_fullname.js"));
        }


        #region sample_transform_to_json_in_compiled_query
        public class JsonQuery: ICompiledQuery<User, string>
        {
            public Expression<Func<IMartenQueryable<User>, string>> QueryIs()
            {
                return _ => _.Where(x => x.FirstName == FirstName)
                .TransformToJson("get_fullname").Single();
            }

            public string FirstName { get; set; }
        }

        #endregion sample_transform_to_json_in_compiled_query

        [Fact]
        public void transform_to_json_in_compiled_query()
        {
            var user = new User { FirstName = "Eric", LastName = "Berry" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.SaveChanges();

                var json = session.Query(new JsonQuery { FirstName = "Eric" });

                json.ShouldBe("{\"fullname\": \"Eric Berry\"}");
            }
        }

        #region sample_using_transform_to_json

        [Fact]
        public void can_select_a_string_field_in_compiled_query()
        {
            var user = new User { FirstName = "Eric", LastName = "Berry" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                session.SaveChanges();

                var name = session.Query<User>().Select(x => x.FirstName)
                    .Single();

                name.ShouldBe("Eric");
            }
        }

        [Fact]
        public void can_transform_to_json()
        {
            var user = new User { FirstName = "Eric", LastName = "Berry" };

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

        #endregion sample_using_transform_to_json

        [Fact]
        public async Task can_transform_to_json_async()
        {
            var user = new User { FirstName = "Eric", LastName = "Berry" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                await session.SaveChangesAsync().ConfigureAwait(false);

                var json = await session.Query<User>()
                    .Where(x => x.Id == user.Id)
                    .TransformToJson("get_fullname").SingleAsync().ConfigureAwait(false);

                json.ShouldBe("{\"fullname\": \"Eric Berry\"}");
            }
        }

        #region sample_transform_to_another_type
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

        #endregion sample_transform_to_another_type

        [Fact]
        public async Task can_transform_to_another_doc_async()
        {
            var user = new User { FirstName = "Eric", LastName = "Berry" };

            using (var session = theStore.OpenSession())
            {
                session.Store(user);
                await session.SaveChangesAsync().ConfigureAwait(false);

                var view = await session.Query<User>()
                    .Where(x => x.Id == user.Id)
                    .TransformTo<FullNameView>("get_fullname").SingleAsync().ConfigureAwait(false);

                view.fullname.ShouldBe("Eric Berry");
            }
        }

        public class FullNameViewQuery: ICompiledQuery<User, FullNameView>
        {
            public Expression<Func<IMartenQueryable<User>, FullNameView>> QueryIs()
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
