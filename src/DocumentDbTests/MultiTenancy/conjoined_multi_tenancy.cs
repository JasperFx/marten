using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentDbTests.SessionMechanics;
using Marten;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql.Tables;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.MultiTenancy;

public class conjoined_multi_tenancy: StoreContext<MultiTenancyFixture>, IClassFixture<MultiTenancyFixture>, IAsyncLifetime
{
    #region sample_delete_all_data_by_tenant

    public static async Task delete_all_tenant_data(IDocumentStore store, CancellationToken token)
    {
        await store.Advanced.DeleteAllTenantDataAsync("AAA", token);
    }

    #endregion

    private readonly ITestOutputHelper _output;
    private readonly Target[] _greens = Target.GenerateRandomData(100).ToArray();

    private readonly Target[] _reds = Target.GenerateRandomData(100).ToArray();
    private readonly Target[] blues = Target.GenerateRandomData(25).ToArray();
    private readonly Target targetBlue1 = Target.Random();
    private readonly Target targetBlue2 = Target.Random();
    private readonly Target targetRed1 = Target.Random();
    private readonly Target targetRed2 = Target.Random();

    public conjoined_multi_tenancy(MultiTenancyFixture fixture): base(fixture)
    {

    }

    public async Task InitializeAsync()
    {
        using (var session = theStore.LightweightSession("Red"))
        {
            session.Store(targetRed1, targetRed2);
            session.Store(new User(), new User());

            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession("Blue"))
        {
            session.Store(targetBlue1, targetBlue2);
            session.Store(new User(), new User());
            await session.SaveChangesAsync();
        }
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task delete_all_data_for_a_tenant()
    {
        await theStore.Advanced.DeleteAllTenantDataAsync("Red", CancellationToken.None);

        using (var session = theStore.LightweightSession("Red"))
        {
            (await session.Query<User>().AnyAsync()).ShouldBeFalse();
            (await session.Query<Target>().AnyAsync()).ShouldBeFalse();
        }

        using (var session = theStore.LightweightSession("Blue"))
        {
            (await session.Query<User>().AnyAsync()).ShouldBeTrue();
            (await session.Query<Target>().AnyAsync()).ShouldBeTrue();
        }
    }

    [Fact]
    public void cannot_load_by_id_across_tenants()
    {
        using (var red = theStore.QuerySession("Red"))
        {
            red.Load<Target>(targetRed1.Id).ShouldNotBeNull();
            red.Load<Target>(targetBlue1.Id).ShouldBeNull();
        }

        using (var blue = theStore.QuerySession("Blue"))
        {
            blue.Load<Target>(targetBlue1.Id).ShouldNotBeNull();
            blue.Load<Target>(targetRed1.Id).ShouldBeNull();
        }
    }

    [Fact]
    public void cannot_load_json_by_id_across_tenants()
    {
        using (var red = theStore.QuerySession("Red"))
        {
            red.Json.FindById<Target>(targetRed1.Id).ShouldNotBeNull();
            red.Json.FindById<Target>(targetBlue1.Id).ShouldBeNull();
        }

        using (var blue = theStore.QuerySession("Blue"))
        {
            blue.Json.FindById<Target>(targetBlue1.Id).ShouldNotBeNull();
            blue.Json.FindById<Target>(targetRed1.Id).ShouldBeNull();
        }
    }


    [Fact]
    public async Task cannot_load_json_by_id_across_tenants_async()
    {
        await using (var red = theStore.QuerySession("Red"))
        {
            (await red.Json.FindByIdAsync<Target>(targetRed1.Id)).ShouldNotBeNull();
            (await red.Json.FindByIdAsync<Target>(targetBlue1.Id)).ShouldBeNull();
        }

        await using (var blue = theStore.QuerySession("Blue"))
        {
            (await blue.Json.FindByIdAsync<Target>(targetBlue1.Id)).ShouldNotBeNull();
            (await blue.Json.FindByIdAsync<Target>(targetRed1.Id)).ShouldBeNull();
        }
    }


    [Fact]
    public async Task cannot_load_by_id_across_tenants_async()
    {
        await using (var red = theStore.QuerySession("Red"))
        {
            (await red.LoadAsync<Target>(targetRed1.Id)).ShouldNotBeNull();
            (await red.LoadAsync<Target>(targetBlue1.Id)).ShouldBeNull();
        }

        await using (var blue = theStore.QuerySession("Blue"))
        {
            (await blue.LoadAsync<Target>(targetBlue1.Id)).ShouldNotBeNull();
            (await blue.LoadAsync<Target>(targetRed1.Id)).ShouldBeNull();
        }
    }

    [Fact]
    public void cannot_load_by_many_id_across_tenants()
    {
        using (var red = theStore.QuerySession("Red"))
        {
            red.LoadMany<Target>(targetRed1.Id, targetRed2.Id).Count.ShouldBe(2);
            red.LoadMany<Target>(targetBlue1.Id, targetBlue1.Id, targetRed1.Id)
                .Single()
                .Id.ShouldBe(targetRed1.Id);
        }
    }


    [Fact]
    public async Task cannot_load_by_many_id_across_tenants_async()
    {
        await using (var red = theStore.QuerySession("Red"))
        {
            (await red.LoadManyAsync<Target>(targetRed1.Id, targetRed2.Id)).Count.ShouldBe(2);
            (await red.LoadManyAsync<Target>(targetBlue1.Id, targetBlue1.Id, targetRed1.Id))
                .Single()
                .Id.ShouldBe(targetRed1.Id);
        }
    }

    [Fact]
    public async Task composite_key_correctly_used_for_upsert_concurrency_check()
    {
        var user = new User { Id = Guid.NewGuid() };

        await using (var session = theStore.LightweightSession("Red"))
        {
            session.Store(user);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("Blue"))
        {
            session.Store(user);
            await session.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task can_add_same_primary_key_to_multiple_tenant()
    {
        var guid = Guid.NewGuid();

        await theStore.EnsureStorageExistsAsync(typeof(Target));
        var existing = await theStore.Tenancy.Default.Database.ExistingTableFor(typeof(Target));
        var mapping = theStore.Options.Storage.MappingFor(typeof(Target));
        var expected = new DocumentTable(mapping);

        var delta = new TableDelta(expected, existing);
        delta.Difference.ShouldBe(SchemaPatchDifference.None);

        await using (var session = theStore.LightweightSession("123"))
        {
            var target = Target.Random();
            target.Id = guid;
            target.String = "123";
            session.ForTenant("123").Store(target);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("abc"))
        {
            var target = Target.Random();
            target.Id = guid;
            target.String = "abc";
            session.ForTenant("abc").Store(target);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("123"))
        {
            var target = session.Load<Target>(guid);
            target.ShouldNotBeNull();
            target.String.ShouldBe("123");
        }

        await using (var session = theStore.LightweightSession("abc"))
        {
            var target = session.Load<Target>(guid);
            target.ShouldNotBeNull();
            target.String.ShouldBe("abc");
        }
    }

    [Fact]
    public async Task can_upsert_in_multi_tenancy()
    {
        using var session = theStore.LightweightSession("123");
        session.Store(Target.GenerateRandomData(10).ToArray());
        await session.SaveChangesAsync();
    }

    [Fact]
    public void can_bulk_insert_with_multi_tenancy_on()
    {
        theStore.BulkInsert("345", Target.GenerateRandomData(100).ToArray());
    }

    [Fact]
    public async Task query_with_batch()
    {
        await theStore.BulkInsertAsync("Red", _reds);
        await theStore.BulkInsertAsync("Green", _greens);

        await using var query = theStore.QuerySession("Red");
        var batch = query.CreateBatchQuery();

        var foundRed = batch.Load<Target>(_reds[0].Id);
        var notFoundGreen = batch.Load<Target>(_greens[0].Id);

        var queryForReds = batch.Query<Target>().Where(x => x.Flag).ToList();

        var groupOfReds = batch.LoadMany<Target>().ById(_reds[0].Id, _reds[1].Id, _greens[0].Id, _greens[1].Id);

        await batch.Execute();

        (await foundRed).ShouldNotBeNull();
        (await notFoundGreen).ShouldBeNull();

        var found = await queryForReds;

        found.Any(x => _greens.Any(t => t.Id == x.Id)).ShouldBeFalse();

        var reds = await groupOfReds;

        reds.Count.ShouldBe(2);
        reds.Any(x => x.Id == _reds[0].Id).ShouldBeTrue();
        reds.Any(x => x.Id == _reds[1].Id).ShouldBeTrue();
    }

    [Fact]
    public void can_query_on_multi_tenanted_and_non_tenanted_documents()
    {
        #region sample_tenancy-mixed-tenancy-non-tenancy-sample

        using var store = DocumentStore.For(opts =>
        {
            opts.DatabaseSchemaName = "mixed_multi_tenants";
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Schema.For<Target>().MultiTenanted(); // tenanted
            opts.Schema.For<User>(); // non-tenanted
            opts.Schema.For<Issue>().MultiTenanted(); // tenanted
        });

        store.Advanced.Clean.DeleteAllDocuments();

        // Add documents to tenant Green
        var greens = Target.GenerateRandomData(10).ToArray();
        store.BulkInsert("Green", greens);

        // Add documents to tenant Red
        var reds = Target.GenerateRandomData(11).ToArray();
        store.BulkInsert("Red", reds);

        // Add non-tenanted documents
        // User is non-tenanted in schema
        var user1 = new User { UserName = "Frank" };
        var user2 = new User { UserName = "Bill" };
        store.BulkInsert(new[] { user1, user2 });

        // Add documents to default tenant
        // Note that schema for Issue is multi-tenanted hence documents will get added
        // to default tenant if tenant is not passed in the bulk insert operation
        var issue1 = new Issue { Title = "Test issue1" };
        var issue2 = new Issue { Title = "Test issue2" };
        store.BulkInsert(new[] { issue1, issue2 });

        // Create a session with tenant Green
        using (var session = store.QuerySession("Green"))
        {
            // Query tenanted document as the tenant passed in session
            session.Query<Target>().Count().ShouldBe(10);

            // Query non-tenanted documents
            session.Query<User>().Count().ShouldBe(2);

            // Query documents in default tenant from a session using tenant Green
            session.Query<Issue>().Count(x => x.TenantIsOneOf(Tenancy.DefaultTenantId)).ShouldBe(2);

            // Query documents from tenant Red from a session using tenant Green
            session.Query<Target>().Count(x => x.TenantIsOneOf("Red")).ShouldBe(11);
        }

        // create a session without passing any tenant, session will use default tenant
        using (var session = store.QuerySession())
        {
            // Query non-tenanted documents
            session.Query<User>().Count().ShouldBe(2);

            // Query documents in default tenant
            // Note that session is using default tenant
            session.Query<Issue>().Count().ShouldBe(2);

            // Query documents on tenant Green
            session.Query<Target>().Count(x => x.TenantIsOneOf("Green")).ShouldBe(10);

            // Query documents on tenant Red
            session.Query<Target>().Count(x => x.TenantIsOneOf("Red")).ShouldBe(11);
        }

        #endregion
    }


    [Fact]
    public void query_within_all_tenants()
    {
        theStore.Advanced.Clean.DeleteAllDocuments();

        var reds = Target.GenerateRandomData(50).ToArray();
        var greens = Target.GenerateRandomData(75).ToArray();
        var blues = Target.GenerateRandomData(25).ToArray();

        theStore.BulkInsert("Red", reds);
        theStore.BulkInsert("Green", greens);
        theStore.BulkInsert("Blue", blues);

        var expected = reds.Concat(greens).Concat(blues)
            .Where(x => x.Flag).Select(x => x.Id).OrderBy(x => x).ToArray();

        using (var query = theStore.QuerySession())
        {
            #region sample_any_tenant

            // query data across all tenants
            var actual = query.Query<Target>().Where(x => x.AnyTenant() && x.Flag)
                .OrderBy(x => x.Id).Select(x => x.Id).ToArray();

            #endregion

            actual.ShouldHaveTheSameElementsAs(expected);
        }
    }

    [Fact]
    public void query_within_all_tenants_with_compound_where()
    {
        theStore.Advanced.Clean.DeleteAllDocuments();

        var reds = Target.GenerateRandomData(50).ToArray();
        var greens = Target.GenerateRandomData(75).ToArray();
        var blues = Target.GenerateRandomData(25).ToArray();

        theStore.BulkInsert("Red", reds);
        theStore.BulkInsert("Green", greens);
        theStore.BulkInsert("Blue", blues);

        var expected = reds.Concat(greens).Concat(blues)
            .Where(x => x.Flag && x.String != null).Select(x => x.Id).OrderBy(x => x).ToArray();

        using (var query = theStore.QuerySession())
        {
            var actual = query.Query<Target>().Where(x => x.AnyTenant() && x.Flag && x.String != null)
                .OrderBy(x => x.Id).Select(x => x.Id).ToArray();

            actual.ShouldHaveTheSameElementsAs(expected);
        }
    }

    [Fact]
    public async Task query_within_selected_tenants()
    {
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();

        var reds = Target.GenerateRandomData(50).ToArray();
        var greens = Target.GenerateRandomData(75).ToArray();
        var blues = Target.GenerateRandomData(25).ToArray();

        await theStore.BulkInsertAsync("Red", reds);
        await theStore.BulkInsertAsync("Green", greens);
        await theStore.BulkInsertAsync("Blue", blues);

        var expected = reds.Concat(greens)
            .Where(x => x.Flag).Select(x => x.Id).OrderBy(x => x).ToArray();

        await using (var query = theStore.QuerySession())
        {
            #region sample_tenant_is_one_of

            // query data for a selected list of tenants
            var actual = await query.Query<Target>().Where(x => x.TenantIsOneOf("Green", "Red") && x.Flag)
                .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();

            #endregion

            actual.ShouldHaveTheSameElementsAs(expected);
        }
    }


    [Fact]
    public async Task tenant_id_on_metadata_async()
    {
        var user1 = new User();
        var user2 = new User();

        await theStore.BulkInsertAsync("Green", new[] { user1 });
        await theStore.BulkInsertAsync("Purple", new[] { user2 });


        await using var session = theStore.QuerySession();

        (await session.MetadataForAsync(user1))
            .TenantId.ShouldBe("Green");

        var metadata = await session.MetadataForAsync(user1);
        metadata.TenantId.ShouldBe("Green");
    }

    [Fact]
    public void document_type_decorated_with_attribute()
    {
        var mapping = DocumentMapping.For<TenantedDoc>();
        mapping.TenancyStyle.ShouldBe(TenancyStyle.Conjoined);
    }

    [Fact]
    public void use_fluent_interface()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Schema.For<User>().MultiTenanted();
        });

        store.StorageFeatures.MappingFor(typeof(User)).TenancyStyle.ShouldBe(TenancyStyle.Conjoined);

        // the "control" to see that the default rules apply otherwise
        store.StorageFeatures.MappingFor(typeof(Target)).TenancyStyle.ShouldBe(TenancyStyle.Single);
    }

    [Fact]
    public async Task will_not_cross_the_streams()
    {
        var user = new User { UserName = "Me" };
        user.Id = Guid.NewGuid();

        using (var red = theStore.LightweightSession("Red"))
        {
            red.Store(user);
            await red.SaveChangesAsync();
        }

        using (var green = theStore.LightweightSession("Green"))
        {
            var greenUser = new User { UserName = "You", Id = user.Id };

            // Nothing should happen here
            green.Store(greenUser);
            await green.SaveChangesAsync();
        }

        // Still got the original data
        using (var query = theStore.QuerySession("Red"))
        {
            query.Load<User>(user.Id).UserName.ShouldBe("Me");
        }
    }

    [Fact]
    public void bulk_insert_respects_tenancy()
    {
        var reds = Target.GenerateRandomData(20).ToArray();
        var greens = Target.GenerateRandomData(15).ToArray();

        theStore.BulkInsert("Red", reds);
        theStore.BulkInsert("Green", greens);

        Guid[] actualReds = null;
        Guid[] actualGreens = null;

        using (var query = theStore.QuerySession("Red"))
        {
            actualReds = query.Query<Target>().Select(x => x.Id).ToArray();
        }

        using (var query = theStore.QuerySession("Green"))
        {
            actualGreens = query.Query<Target>().Select(x => x.Id).ToArray();
        }

        actualGreens.Intersect(actualReds).Any().ShouldBeFalse();
    }


    [Fact]
    public async Task write_to_tenant()
    {
        var reds = Target.GenerateRandomData(50).ToArray();
        var greens = Target.GenerateRandomData(75).ToArray();
        var blues = Target.GenerateRandomData(25).ToArray();

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.ForTenant("Red").Store(reds.AsEnumerable());
            session.ForTenant("Green").Store(greens.AsEnumerable());
            session.ForTenant("Blue").Store(blues.AsEnumerable());

            await session.SaveChangesAsync();
        }

        await using (var red = theStore.QuerySession("Red"))
        {
            red.Query<Target>().Count().ShouldBe(50);
        }

        await using (var green = theStore.QuerySession("Green"))
        {
            green.Query<Target>().Count().ShouldBe(75);
        }

        await using (var blue = theStore.QuerySession("Blue"))
        {
            blue.Query<Target>().Count().ShouldBe(25);
        }
    }


    [Fact]
    public async Task write_to_tenant_with_explicitly_overridden_tenant()
    {
        var reds = Target.GenerateRandomData(50).ToArray();
        var greens = Target.GenerateRandomData(75).ToArray();
        var blues = Target.GenerateRandomData(25).ToArray();

        theStore.Advanced.Clean.DeleteAllDocuments();

        using (var session = theStore.LightweightSession())
        {
            session.ForTenant("Red").Store(reds);
            session.ForTenant("Green").Store(greens);
            session.ForTenant("Blue").Store(blues);

            await session.SaveChangesAsync();
        }

        using (var red = theStore.QuerySession("Red"))
        {
            red.Query<Target>().Count().ShouldBe(50);
        }

        using (var green = theStore.QuerySession("Green"))
        {
            green.Query<Target>().Count().ShouldBe(75);
        }

        using (var blue = theStore.QuerySession("Blue"))
        {
            blue.Query<Target>().Count().ShouldBe(25);
        }
    }

    [MultiTenanted]
    public class TenantedDoc
    {
        public Guid Id;
    }

    [Fact]
    public async Task can_delete_a_document_by_tenant()
    {
        var target = new Target { Id = Guid.NewGuid() };

        await using (var session = theStore.LightweightSession("Red"))
        {
            session.Store(target);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("Blue"))
        {
            session.Store(target);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.ForTenant("Blue").Delete(target);
            await session.SaveChangesAsync();
        }

        var red = theStore.QuerySession("Blue");
        (await red.LoadAsync<Target>(target.Id)).ShouldBeNull();

        var blue = theStore.QuerySession("Red");
        (await blue.LoadAsync<Target>(target.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task can_delete_a_document_by_id_and_tenant_Guid()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));

        var target = new Target { Id = Guid.NewGuid() };

        await using (var session = theStore.LightweightSession("Red"))
        {
            session.Store(target);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("Blue"))
        {
            session.Store(target);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Logger = new TestOutputMartenLogger(_output);
            session.ForTenant("Blue").Delete<Target>(target.Id);
            await session.SaveChangesAsync();
        }

        var blue = theStore.QuerySession("Blue");
        blue.Logger = new TestOutputMartenLogger(_output);

        (await blue.LoadAsync<Target>(target.Id)).ShouldBeNull();

        var red = theStore.QuerySession("Red");
        (await red.LoadAsync<Target>(target.Id)).ShouldNotBeNull();
    }


    [Fact]
    public async Task can_delete_a_document_by_id_and_tenant_int()
    {
        var target = new IntDoc { Id = 5 };

        await using (var session = theStore.LightweightSession("Red"))
        {
            session.Store(target);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("Blue"))
        {
            session.Store(target);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Logger = new TestOutputMartenLogger(_output);
            session.ForTenant("Blue").Delete<IntDoc>(target.Id);
            await session.SaveChangesAsync();
        }

        var red = theStore.QuerySession("Blue");
        (await red.LoadAsync<IntDoc>(target.Id)).ShouldBeNull();

        var blue = theStore.QuerySession("Red");
        (await blue.LoadAsync<IntDoc>(target.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task can_delete_a_document_by_id_and_tenant_long()
    {
        var target = new LongDoc { Id = 5 };

        await using (var session = theStore.LightweightSession("Red"))
        {
            session.Store(target);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("Blue"))
        {
            session.Store(target);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Logger = new TestOutputMartenLogger(_output);
            session.ForTenant("Blue").Delete<LongDoc>(target.Id);
            await session.SaveChangesAsync();
        }

        var red = theStore.QuerySession("Blue");
        (await red.LoadAsync<LongDoc>(target.Id)).ShouldBeNull();

        var blue = theStore.QuerySession("Red");
        (await blue.LoadAsync<LongDoc>(target.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task can_delete_a_document_by_id_and_tenant_string()
    {
        var target = new StringDoc { Id = Guid.NewGuid().ToString() };

        await using (var session = theStore.LightweightSession("Red"))
        {
            session.Store(target);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("Blue"))
        {
            session.Store(target);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Logger = new TestOutputMartenLogger(_output);
            session.ForTenant("Blue").Delete<StringDoc>(target.Id);
            await session.SaveChangesAsync();
        }

        var red = theStore.QuerySession("Blue");
        (await red.LoadAsync<StringDoc>(target.Id)).ShouldBeNull();

        var blue = theStore.QuerySession("Red");
        (await blue.LoadAsync<StringDoc>(target.Id)).ShouldNotBeNull();
    }
}
