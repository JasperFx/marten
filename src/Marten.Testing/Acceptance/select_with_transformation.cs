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
            StoreOptions(_ =>
            {
                this.InProfile(TestingContracts.CamelCase, () =>
                {
                    _.Transforms.LoadFile("get_fullname_camelCase.js", "get_fullname");
                }).Otherwise(() =>
                {
                    _.Transforms.LoadFile("get_fullname.js", "get_fullname");
                });
            });
        }

        public void load_transformation()
        {
    var store = DocumentStore.For(_ =>
    {
        _.Connection(ConnectionSource.ConnectionString);

        // Let Marten derive the transform name
        // from the file name
        _.Transforms.LoadFile("get_fullname.js");

        // or override the transform name
        _.Transforms.LoadFile("get_fullname.js", "fullname");
    });

            store.Dispose();
        }

        // SAMPLE: transform_to_json_in_compiled_query
        public class JsonQuery : ICompiledQuery<User, string>
        {
            public Expression<Func<IQueryable<User>, string>> QueryIs()
            {
                return _ => _.Where(x => x.FirstName == FirstName)
                .TransformToJson("get_fullname").Single();
            }

            public string FirstName { get; set; }
        }
        // ENDSAMPLE

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

        // SAMPLE: using_transform_to_json


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
        // ENDSAMPLE

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

        // SAMPLE: transform_to_another_type
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
        // ENDSAMPLE

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