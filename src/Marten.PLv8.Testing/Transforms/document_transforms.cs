using System;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.PLv8.Testing.Transforms;

public class JsTransformsFixture : StoreFixture
{
    public JsTransformsFixture() : base("js")
    {
        Options.DatabaseSchemaName = "js";

        Options.UseJavascriptTransformsAndPatching(x => x.LoadFile("default_username.js"));

        Options.Schema.For<MultiTenantUser>().MultiTenanted();
    }
}

public class MultiTenantUser: User
{

}

public class document_transforms: StoreContext<JsTransformsFixture>, IClassFixture<JsTransformsFixture>, IAsyncLifetime
{
    public document_transforms(JsTransformsFixture fixture) : base(fixture)
    {

    }

    public Task InitializeAsync()
    {
        return theStore.Advanced.Clean.DeleteAllDocumentsAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }


    internal static void example()
    {
        #region sample_loading_js_transform_files
        var store = DocumentStore.For(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);

            _.UseJavascriptTransformsAndPatching(transforms =>
            {
                // Let Marten derive the transform name from the filename
                transforms.LoadFile("get_fullname.js");

                // Explicitly define the transform name yourself
                transforms.LoadFile("default_username.js", "set_default_username");
            });


        });
        #endregion

        transform_example(store);

        store.Dispose();
    }

    #region sample_transform_example
    private static async Task transform_example(IDocumentStore store)
    {
        await store.TransformAsync(x =>
        {
            // Transform User documents with a filter
            x.Where<User>("default_username", x => x.UserName == null);

            // Transform a single User document by Id
            x.Document<User>("default_username", Guid.NewGuid());

            // Transform all User documents
            x.All<User>("default_username");

            // Only transform documents from the "tenant1" tenant
            x.Tenant<User>("default_username", "tenant1");

            // Only transform documents from the named tenants
            x.Tenants<User>("default_username", "tenant1", "tenant2");
        });


    }

    #endregion

    [Fact] //-- Unreliable on CI
    public async Task use_transform_in_production_mode()
    {
        await theStore.Tenancy.Default.Database.EnsureStorageExistsAsync(typeof(User));
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await theStore.TransformAsync(x => x.All<User>("default_username"));

    }

    [Fact]
    public async Task transform_all_documents()
    {
        var user1 = new User { FirstName = "Jeremy", LastName = "Miller" };
        var user2 = new User { FirstName = "Corey", LastName = "Kaylor" };
        var user3 = new User { FirstName = "Tim", LastName = "Cools" };

        await theStore.BulkInsertAsync(new User[] { user1, user2, user3 });

        await theStore.TransformAsync(x => x.All<User>("default_username"));

        using (var session = theStore.QuerySession())
        {
            (await session.LoadAsync<User>(user1.Id)).UserName.ShouldBe("jeremy.miller");
            (await session.LoadAsync<User>(user2.Id)).UserName.ShouldBe("corey.kaylor");
            (await session.LoadAsync<User>(user3.Id)).UserName.ShouldBe("tim.cools");
        }
    }

    [Fact]
    public async Task transform_all_documents_in_session()
    {
        var user1 = new User { FirstName = "Jeremy", LastName = "Miller" };
        var user2 = new User { FirstName = "Corey", LastName = "Kaylor" };
        var user3 = new User { FirstName = "Tim", LastName = "Cools" };

        await theStore.BulkInsertAsync(new User[] { user1, user2, user3 });

        await theStore.TransformAsync(x => x.All<User>("default_username"));

        using (var session = theStore.LightweightSession())
        {
            (await session.LoadAsync<User>(user1.Id)).UserName.ShouldBe("jeremy.miller");
            (await session.LoadAsync<User>(user2.Id)).UserName.ShouldBe("corey.kaylor");
            (await session.LoadAsync<User>(user3.Id)).UserName.ShouldBe("tim.cools");
        }
    }

    [Fact]
    public async Task transform_for_tenants()
    {

        var user1 = new MultiTenantUser() { FirstName = "Jeremy", LastName = "Miller" };
        var user2 = new MultiTenantUser { FirstName = "Corey", LastName = "Kaylor" };
        var user3 = new MultiTenantUser { FirstName = "Tim", LastName = "Cools", UserName = "NotTransformed" };

        await theStore.BulkInsertAsync("Purple", new MultiTenantUser[] { user1, user2 });
        await theStore.BulkInsertAsync("Orange", new MultiTenantUser[] { user3 });

        await theStore.TransformAsync(x => x.Tenant<MultiTenantUser>("default_username", "Purple"));

        using (var query = theStore.QuerySession("Purple"))
        {
            (await query.LoadAsync<MultiTenantUser>(user1.Id)).UserName.ShouldBe("jeremy.miller");
        }

        using (var query = theStore.QuerySession("Orange"))
        {
            (await query.LoadAsync<MultiTenantUser>(user3.Id)).UserName.ShouldBe("NotTransformed");
        }
    }

    [Fact]
    public async Task transform_a_single_document()
    {
        var user1 = new User { FirstName = "Jeremy", LastName = "Miller" };
        var user2 = new User { FirstName = "Corey", LastName = "Kaylor", UserName = "user2" };
        var user3 = new User { FirstName = "Tim", LastName = "Cools", UserName = "user3" };

        await theStore.BulkInsertAsync(new User[] { user1, user2, user3 });

        await theStore.TransformAsync(x => x.Document<User>("default_username", user1.Id));

        using (var session = theStore.QuerySession())
        {
            (await session.LoadAsync<User>(user1.Id)).UserName.ShouldBe("jeremy.miller");

            // no updates to these
            (await session.LoadAsync<User>(user2.Id)).UserName.ShouldBe("user2");
            (await session.LoadAsync<User>(user3.Id)).UserName.ShouldBe("user3");
        }
    }

    [Fact]
    public async Task transform_a_single_document_in_session()
    {
        var user1 = new User { FirstName = "Jeremy", LastName = "Miller" };
        var user2 = new User { FirstName = "Corey", LastName = "Kaylor", UserName = "user2" };
        var user3 = new User { FirstName = "Tim", LastName = "Cools", UserName = "user3" };

        await theStore.BulkInsertAsync(new[] { user1, user2, user3 });

        await theStore.TransformAsync(x => x.Document<User>("default_username", user1.Id));

        using (var session = theStore.QuerySession())
        {
            (await session.LoadAsync<User>(user1.Id)).UserName.ShouldBe("jeremy.miller");

            // no updates to these
            (await session.LoadAsync<User>(user2.Id)).UserName.ShouldBe("user2");
            (await session.LoadAsync<User>(user3.Id)).UserName.ShouldBe("user3");
        }
    }

    [Fact]
    public async Task transform_with_where_clause()
    {
        var user1 = new User { FirstName = "Jeremy", LastName = "Miller" };
        var user2 = new User { FirstName = "Corey", LastName = "Kaylor", UserName = "user2" };
        var user3 = new User { FirstName = "Tim", LastName = "Cools", UserName = "user3" };

        await theStore.BulkInsertAsync(new User[] { user1, user2, user3 });

        await theStore.TransformAsync(x => x.Where<User>("default_username", x => x.FirstName == user1.FirstName));

        using (var session = theStore.QuerySession())
        {
            (await session.LoadAsync<User>(user1.Id)).UserName.ShouldBe("jeremy.miller");

            // no updates to these
            (await session.LoadAsync<User>(user2.Id)).UserName.ShouldBe("user2");
            (await session.LoadAsync<User>(user3.Id)).UserName.ShouldBe("user3");
        }
    }

    [Fact]
    public async Task transform_with_where_clause_in_session()
    {
        var user1 = new User { FirstName = "Jeremy", LastName = "Miller" };
        var user2 = new User { FirstName = "Corey", LastName = "Kaylor", UserName = "user2" };
        var user3 = new User { FirstName = "Tim", LastName = "Cools", UserName = "user3" };

        await theStore.BulkInsertAsync(new User[] { user1, user2, user3 });

        await theStore.TransformAsync(x => x.Where<User>("default_username", x => x.FirstName == user1.FirstName));

        using (var session = theStore.QuerySession())
        {
            (await session.LoadAsync<User>(user1.Id)).UserName.ShouldBe("jeremy.miller");

            // no updates to these
            (await session.LoadAsync<User>(user2.Id)).UserName.ShouldBe("user2");
            (await session.LoadAsync<User>(user3.Id)).UserName.ShouldBe("user3");
        }
    }
}
