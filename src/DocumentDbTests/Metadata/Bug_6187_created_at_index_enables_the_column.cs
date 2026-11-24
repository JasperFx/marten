using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Metadata;

[IndexedCreatedAt]
public class Bug6187AttributeIndexedDoc
{
    public Guid Id { get; set; }
}

public class Bug6187FluentIndexedDoc
{
    public Guid Id { get; set; }
}

/// <summary>
///     #6187: the mt_created_at column is opt-in, and declaring an index on it did not turn it on, so
///     <c>IndexCreatedAt()</c> or <c>[IndexedCreatedAt]</c> on their own failed schema creation on the missing
///     column. Declaring the index now enables the column, as mapping a metadata member does.
/// </summary>
public class Bug_6187_created_at_index_enables_the_column: BugIntegrationContext
{
    [Fact]
    public async Task the_attribute_enables_the_column_it_indexes()
    {
        StoreOptions(_ => { });

        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Bug6187AttributeIndexedDoc));

        var doc = new Bug6187AttributeIndexedDoc();
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var metadata = await theSession.MetadataForAsync(doc);
        metadata.ShouldNotBeNull();
        metadata.CreatedAt.ShouldNotBe(default);

        var table = await theStore.Tenancy.Default.Database.ExistingTableFor(typeof(Bug6187AttributeIndexedDoc));
        table.ShouldNotBeNull();
        table.HasColumn(SchemaConstants.CreatedAtColumn).ShouldBeTrue();
        table.Indexes.ShouldContain(x => x.Columns.Contains(SchemaConstants.CreatedAtColumn));
    }

    [Fact]
    public async Task the_fluent_index_enables_the_column_it_indexes()
    {
        StoreOptions(opts => opts.Schema.For<Bug6187FluentIndexedDoc>().IndexCreatedAt());

        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Bug6187FluentIndexedDoc));

        var doc = new Bug6187FluentIndexedDoc();
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var metadata = await theSession.MetadataForAsync(doc);
        metadata.ShouldNotBeNull();
        metadata.CreatedAt.ShouldNotBe(default);

        var table = await theStore.Tenancy.Default.Database.ExistingTableFor(typeof(Bug6187FluentIndexedDoc));
        table.ShouldNotBeNull();
        table.HasColumn(SchemaConstants.CreatedAtColumn).ShouldBeTrue();
        table.Indexes.ShouldContain(x => x.Columns.Contains(SchemaConstants.CreatedAtColumn));
    }
}
