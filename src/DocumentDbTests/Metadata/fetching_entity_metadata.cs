using System;
using System.Threading.Tasks;
using DocumentDbTests.Concurrency;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Metadata;

public class fetching_entity_metadata: OneOffConfigurationsContext
{
    [Fact]
    public void total_miss_returns_null()
    {
        var shop = new CoffeeShop();

        theSession.MetadataFor(shop)
            .ShouldBeNull();

    }

    #region sample_resolving_metadata
    [Fact]
    public async Task hit_returns_values()
    {
        var shop = new CoffeeShop();

        using (var session = theStore.LightweightSession())
        {
            session.Store(shop);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            var metadata = session.MetadataFor(shop);

            metadata.ShouldNotBeNull();
            metadata.CurrentVersion.ShouldNotBe(Guid.Empty);
            metadata.CreatedAt.ShouldBe(default);
            metadata.LastModified.ShouldNotBe(default);
            metadata.DotNetType.ShouldBe(typeof(CoffeeShop).FullName);
            metadata.DocumentType.ShouldBeNull();
            metadata.Deleted.ShouldBeFalse();
            metadata.DeletedAt.ShouldBeNull();
        }
    }

    #endregion

    [Fact]
    public async Task hit_returns_values_async()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Shop>().SoftDeleted().AddSubClass<CoffeeShop>();
        });

        var shop = new CoffeeShop();

        await using (var session = theStore.LightweightSession())
        {
            session.Store(shop);
            await session.SaveChangesAsync();

            session.Delete(shop);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var metadata = await query.MetadataForAsync(shop);

        metadata.ShouldNotBeNull();
        metadata.CurrentVersion.ShouldNotBe(Guid.Empty);
        metadata.CreatedAt.ShouldBe(default);
        metadata.LastModified.ShouldNotBe(default);
        metadata.DotNetType.ShouldBe(typeof(CoffeeShop).FullName);
        metadata.DocumentType.ShouldBe("coffee_shop");
        metadata.Deleted.ShouldBeTrue();
        metadata.DeletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task created_timestamp_metadata_returns_default()
    {
        var shop = new CoffeeShop();

        using (var session = theStore.LightweightSession())
        {
            session.Store(shop);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            var metadata = session.MetadataFor(shop);
            metadata.ShouldNotBeNull();

            metadata.CreatedAt.ShouldBe(default);
        }
    }

    [Fact]
    public async Task created_timestamp_metadata_returns_default_async()
    {
        var shop = new CoffeeShop();

        await using (var session = theStore.LightweightSession())
        {
            session.Store(shop);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var metadata = await query.MetadataForAsync(shop);

        metadata.CreatedAt.ShouldBe(default);
    }

    [Fact]
    public async Task created_timestamp_metadata_returns_timestamp()
    {
        StoreOptions(_ =>
        {
            _.Policies.ForAllDocuments(o => o.Metadata.CreatedAt.Enabled = true);
        });

        var shop = new CoffeeShop();

        using (var session = theStore.LightweightSession())
        {
            session.Store(shop);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            var metadata = session.MetadataFor(shop);
            metadata.ShouldNotBeNull();

            metadata.CreatedAt.ShouldNotBe(default);
        }
    }

    [Fact]
    public async Task created_timestamp_metadata_returns_timestamp_async()
    {
        StoreOptions(_ =>
        {
            _.Policies.ForAllDocuments(o => o.Metadata.CreatedAt.Enabled = true);
        });

        var shop = new CoffeeShop();

        await using (var session = theStore.LightweightSession())
        {
            session.Store(shop);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var metadata = await query.MetadataForAsync(shop);

        metadata.CreatedAt.ShouldNotBe(default);
    }
}
