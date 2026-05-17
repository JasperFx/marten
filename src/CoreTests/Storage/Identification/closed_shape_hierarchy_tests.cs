using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Storage.Identification.ClosedShape;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike M11: validates hierarchical / sub-class behavior on the
/// closed-shape document storage. AddSubClass adds an mt_doc_type
/// discriminator column; writes capture the runtime type's alias;
/// reads from the root return polymorphic instances; queries scoped to
/// a sub-class via SubClassDocumentStorage filter on the alias.
/// </summary>
public class closed_shape_hierarchy_tests: BugIntegrationContext
{
    private DocumentStore HierarchyStore()
        => StoreOptions(opts =>
        {
            opts.UseClosedShapeDocumentStorage = true;
            opts.Schema.For<HierShop>().AddSubClass<HierCoffeeShop>();
        });

    [Fact]
    public async Task subclass_round_trips_via_root_storage()
    {
        var store = HierarchyStore();

        var id = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store<HierShop>(new HierCoffeeShop { Id = id, Name = "Blue Bottle", Roast = "dark" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var loaded = await query.LoadAsync<HierShop>(id);
        loaded.ShouldNotBeNull();
        loaded.ShouldBeOfType<HierCoffeeShop>();
        loaded.Name.ShouldBe("Blue Bottle");
        ((HierCoffeeShop)loaded).Roast.ShouldBe("dark");
    }

    [Fact]
    public async Task subclass_query_filters_by_discriminator()
    {
        var store = HierarchyStore();

        await using (var session = store.LightweightSession())
        {
            session.Store<HierShop>(new HierShop { Id = Guid.NewGuid(), Name = "plain" });
            session.Store<HierShop>(new HierCoffeeShop { Id = Guid.NewGuid(), Name = "coffee", Roast = "medium" });
            session.Store<HierShop>(new HierCoffeeShop { Id = Guid.NewGuid(), Name = "another coffee", Roast = "light" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var coffees = await query.Query<HierCoffeeShop>().ToListAsync();
        coffees.Count.ShouldBe(2);
        coffees.ShouldAllBe(c => c is HierCoffeeShop);
    }

    [Fact]
    public async Task subclass_load_by_id_returns_null_for_wrong_subclass()
    {
        var store = HierarchyStore();

        var plainId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Store<HierShop>(new HierShop { Id = plainId, Name = "plain shop" });
            await session.SaveChangesAsync();
        }

        // LoadAsync<HierCoffeeShop> for a plain HierShop row should
        // return null — SubClassDocumentStorage.LoadAsync casts and
        // returns default when the type doesn't match.
        await using var query = store.QuerySession();
        var maybeCoffee = await query.LoadAsync<HierCoffeeShop>(plainId);
        maybeCoffee.ShouldBeNull();

        // ...but querying as the root returns the plain shop.
        var asShop = await query.LoadAsync<HierShop>(plainId);
        asShop.ShouldNotBeNull();
        asShop.GetType().ShouldBe(typeof(HierShop));
    }

    [Fact]
    public async Task root_query_returns_polymorphic_instances()
    {
        var store = HierarchyStore();

        await using (var session = store.LightweightSession())
        {
            session.Store<HierShop>(new HierShop { Id = Guid.NewGuid(), Name = "root" });
            session.Store<HierShop>(new HierCoffeeShop { Id = Guid.NewGuid(), Name = "sub", Roast = "espresso" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var all = await query.Query<HierShop>().ToListAsync();
        all.Count.ShouldBe(2);

        var byName = all.ToDictionary(x => x.Name);
        byName["root"].GetType().ShouldBe(typeof(HierShop));
        byName["sub"].GetType().ShouldBe(typeof(HierCoffeeShop));
        ((HierCoffeeShop)byName["sub"]).Roast.ShouldBe("espresso");
    }

    [Fact]
    public void IsSupported_accepts_hierarchical_root_mapping()
    {
        var store = StoreOptions(opts =>
        {
            opts.Schema.For<HierShop>().AddSubClass<HierCoffeeShop>();
        });

        var rootMapping = (Marten.Schema.DocumentMapping)store.Options.Storage.FindMapping(typeof(HierShop));
        ClosedShapeRegistration.IsSupported(rootMapping).ShouldBeTrue();
    }
}

public class HierShop
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class HierCoffeeShop: HierShop
{
    public string Roast { get; set; } = string.Empty;
}
