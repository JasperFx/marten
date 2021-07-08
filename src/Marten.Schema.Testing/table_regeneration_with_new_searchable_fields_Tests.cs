using System.Collections.Generic;
using System.Linq;
using Weasel.Postgresql;
using Marten.Schema.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace Marten.Schema.Testing
{
    public class table_regeneration_with_new_searchable_fields_Tests : IntegrationContext
    {
        [Fact]
        public void do_not_lose_data_if_only_change_is_searchable_field()
        {
            var user1 = new User {FirstName = "Jeremy"};
            var user2 = new User {FirstName = "Max"};
            var user3 = new User {FirstName = "Declan"};

            theStore.BulkInsert(new User[] {user1, user2, user3});

            using (var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>().Duplicate(x => x.FirstName);
            }))
            {
                using (var session = store2.QuerySession())
                {
                    session.Query<User>().Count().ShouldBe(3);

                    var list = new List<string>();

                    using (
                        var reader =
                            session.Connection.CreateCommand()
                                .Sql("select first_name from mt_doc_user")
                                .ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(reader.GetString(0));
                        }


                    }

                    list.OrderBy(x => x).ShouldHaveTheSameElementsAs("Declan", "Jeremy", "Max");

                    session.Query<User>().Single(x => x.FirstName == "Jeremy").ShouldNotBeNull();

                }
            }
        }

        [Fact]
        public void do_not_lose_data_if_only_change_is_searchable_field_for_AutoCreateMode_is_CreateOrUpdate()
        {
            var user1 = new User { FirstName = "Jeremy" };
            var user2 = new User { FirstName = "Max" };
            var user3 = new User { FirstName = "Declan" };

            theStore.BulkInsert(new User[] { user1, user2, user3 });

            using (var store2 = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>().Duplicate(x => x.FirstName);
            }))
            {
                using (var session = store2.QuerySession())
                {
                    session.Query<User>().Count().ShouldBe(3);

                    var list = new List<string>();

                    using (
                        var reader =
                            session.Connection.CreateCommand()
                                .Sql("select first_name from mt_doc_user")
                                .ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(reader.GetString(0));
                        }


                    }

                    list.OrderBy(x => x).ShouldHaveTheSameElementsAs("Declan", "Jeremy", "Max");

                    SpecificationExtensions.ShouldNotBeNull(session.Query<User>().Where(x => x.FirstName == "Jeremy").Single());

                }
            }
        }

        public table_regeneration_with_new_searchable_fields_Tests()
        {
        }
    }
}
