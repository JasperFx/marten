using System;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class document_transforms : IntegratedFixture
    {
        public document_transforms()
        {
            StoreOptions(_ =>
            {
                _.Transforms.LoadFile("default_username.js");
            });
        }


        public void example()
        {
            // SAMPLE: loading_js_transform_files
    var store = DocumentStore.For(_ =>
    {
        _.Connection(ConnectionSource.ConnectionString);

        // Let Marten derive the transform name from the filename
        _.Transforms.LoadFile("get_fullname.js");

        // Explicitly define the transform name yourself
        _.Transforms.LoadFile("default_username.js", "set_default_username");
    });
            // ENDSAMPLE

    
    transform_example(store);

            store.Dispose();
        }

        // SAMPLE: transform_example
        private static void transform_example(IDocumentStore store)
        {
            // Transform User documents with a filter
            store.Transform.Where<User>("default_username", x => x.UserName == null);

            // Transform a single User document by Id
            store.Transform.Document<User>("default_username", Guid.NewGuid());

            // Transform all User documents
            store.Transform.All<User>("default_username");

            // Only transform documents from the "tenant1" tenant
            store.Transform.Tenant<User>("default_username", "tenant1");

            // Only transform documents from the named tenants
            store.Transform.Tenants<User>("default_username", "tenant1", "tenant2");
        }
        // ENDSAMPLE



        [Fact] //-- Unreliable on CI
        public void use_transform_in_production_mode()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Transforms.LoadFile("default_username.js");
                _.AutoCreateSchemaObjects = AutoCreate.None;
            }))
            {
                store.Transform.All<User>("default_username");
            }
        }

        [Fact]
        public void transform_all_documents()
        {
            var user1 = new User {FirstName = "Jeremy", LastName = "Miller"};
            var user2 = new User {FirstName = "Corey", LastName = "Kaylor"};
            var user3 = new User {FirstName = "Tim", LastName = "Cools"};

            theStore.BulkInsert(new User[] {user1, user2, user3});

            theStore.Transform.All<User>("default_username");


            using (var session = theStore.QuerySession())
            {
                session.Load<User>(user1.Id).UserName.ShouldBe("jeremy.miller");
                session.Load<User>(user2.Id).UserName.ShouldBe("corey.kaylor");
                session.Load<User>(user3.Id).UserName.ShouldBe("tim.cools");
            }
        }

        [Fact]
        public void transform_for_tenants()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>().MultiTenanted();
                _.Transforms.LoadFile("default_username.js");
            });

            var user1 = new User { FirstName = "Jeremy", LastName = "Miller" };
            var user2 = new User { FirstName = "Corey", LastName = "Kaylor" };
            var user3 = new User { FirstName = "Tim", LastName = "Cools", UserName = "NotTransformed"};

            theStore.BulkInsert("Purple",new User[]{user1, user2});
            theStore.BulkInsert("Orange",new User[]{user3});

            theStore.Transform.Tenant<User>("default_username", "Purple");

            using (var query = theStore.QuerySession("Purple"))
            {
                query.Load<User>(user1.Id).UserName.ShouldBe("jeremy.miller");
            }

            using (var query = theStore.QuerySession("Orange"))
            {
                query.Load<User>(user3.Id).UserName.ShouldBe("NotTransformed");
            }
        }

        

        [Fact]
        public void transform_a_single_document()
        {
            var user1 = new User { FirstName = "Jeremy", LastName = "Miller" };
            var user2 = new User { FirstName = "Corey", LastName = "Kaylor", UserName = "user2"};
            var user3 = new User { FirstName = "Tim", LastName = "Cools", UserName = "user3"};

            theStore.BulkInsert(new User[] { user1, user2, user3 });


            theStore.Transform.Document<User>("default_username", user1.Id);

            using (var session = theStore.QuerySession())
            {
                session.Load<User>(user1.Id).UserName.ShouldBe("jeremy.miller");

                // no updates to these
                session.Load<User>(user2.Id).UserName.ShouldBe("user2");
                session.Load<User>(user3.Id).UserName.ShouldBe("user3");
            }
        }

        [Fact]
        public void transform_with_where_clause()
        {
            var user1 = new User { FirstName = "Jeremy", LastName = "Miller" };
            var user2 = new User { FirstName = "Corey", LastName = "Kaylor", UserName = "user2" };
            var user3 = new User { FirstName = "Tim", LastName = "Cools", UserName = "user3" };

            theStore.BulkInsert(new User[] { user1, user2, user3 });


            theStore.Transform.Where<User>("default_username", x => x.FirstName == user1.FirstName);

            using (var session = theStore.QuerySession())
            {
                session.Load<User>(user1.Id).UserName.ShouldBe("jeremy.miller");

                // no updates to these
                session.Load<User>(user2.Id).UserName.ShouldBe("user2");
                session.Load<User>(user3.Id).UserName.ShouldBe("user3");
            }
        }
    }
}