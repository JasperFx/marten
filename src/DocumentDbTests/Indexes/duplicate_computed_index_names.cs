using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Indexes;

/// <summary>
/// #6019: a second Index() that resolves to the same index name used to be accepted and then fail inside
/// a migration with "An item with the same key has already been added". It now throws while the document
/// mapping is configured, and says which document, which member and which index.
/// </summary>
public class duplicate_computed_index_names: OneOffConfigurationsContext
{
    private Exception configurationFailure(Action<StoreOptions> configure)
    {
        return Should.Throw<InvalidOperationException>(() =>
        {
            StoreOptions(configure);
            theStore.StorageFeatures.MappingFor(typeof(IndexedOrder));
        });
    }

    [Fact]
    public void the_same_index_twice_throws()
    {
        var ex = configurationFailure(opts =>
        {
            opts.Schema.For<IndexedOrder>().Index(x => x.CustomerEmail);
            opts.Schema.For<IndexedOrder>().Index(x => x.CustomerEmail);
        });

        ex.Message.ShouldContain(typeof(IndexedOrder).FullName!);
        ex.Message.ShouldContain("CustomerEmail");
        ex.Message.ShouldContain("mt_doc_indexedorder_idx_customer_email");
        ex.Message.ShouldContain("x.Name");
    }

    [Fact]
    public void a_lower_cased_index_over_an_indexed_member_throws()
    {
        var ex = configurationFailure(opts =>
        {
            opts.Schema.For<IndexedOrder>().Index(x => x.CustomerEmail);

            // e.g. in another module's configuration
            opts.Schema.For<IndexedOrder>()
                .Index(x => x.CustomerEmail, x => x.Casing = ComputedIndex.Casings.Lower);
        });

        ex.Message.ShouldContain(typeof(IndexedOrder).FullName!);
        ex.Message.ShouldContain("CustomerEmail");
        ex.Message.ShouldContain("mt_doc_indexedorder_idx_customer_email");
    }

    [Fact]
    public async Task two_indexes_over_one_member_with_their_own_names_both_work()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<IndexedOrder>().Index(x => x.CustomerEmail);
            opts.Schema.For<IndexedOrder>().Index(x => x.CustomerEmail, x =>
            {
                x.Casing = ComputedIndex.Casings.Lower;
                x.Name = "idx_indexedorder_customer_email_lower";
            });
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var names = theStore.StorageFeatures.MappingFor(typeof(IndexedOrder)).Indexes
            .OfType<ComputedIndex>().Select(x => x.Name).ToArray();
        names.ShouldBe(["mt_doc_indexedorder_idx_customer_email", "idx_indexedorder_customer_email_lower"], ignoreOrder: true);

        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}

public class IndexedOrder
{
    public Guid Id { get; set; }
    public string CustomerEmail { get; set; } = "";
}
