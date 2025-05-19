using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3665_compiling_with_private_members : BugIntegrationContext
{
    [Fact]
    public async Task try_to_compile_self_aggregate()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<Product>(SnapshotLifecycle.Inline);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var id = Guid.NewGuid().ToString();
        theSession.Events.StartStream<Product>(id, new ProductCreated(id, "Shoes"));
        await theSession.SaveChangesAsync();

        var product = await theSession.LoadAsync<Product>(id);
        product.Name.ShouldBe("Shoes");
    }
}

public record ProductCreated(string Id, string Name);

public record ProductNameChanged(string NewName);

public record ProductRemoved;


public class Product
{
    public string Id { get; set; }
    public string Name { get; set; }

    public Product()    {            }

    public Product(ProductCreated cr)
    {
        Id = cr.Id;
        Name = cr.Name;
    }

    private void Apply(ProductNameChanged ev) => Name = ev.NewName;

    private bool ShouldDelete(ProductRemoved ev) => true;
}


