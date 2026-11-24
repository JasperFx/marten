using System;
using System.Threading.Tasks;
using Marten;
using Marten.Linq.SoftDeletes;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Metadata;

[SoftDeleted]
public class Bug6194SoftDeletedDoc
{
    public Guid Id { get; set; }

    [SoftDeletedMetadata]
    public bool Deleted { get; set; }
}

/// <summary>
///     #6194: the attribute for mt_deleted is a class named IsSoftDeletedMetadataAttributeAttribute, so it had to be
///     written [IsSoftDeletedMetadataAttribute]. [SoftDeletedMetadata] is the correctly named form.
/// </summary>
public class soft_deleted_metadata_attribute: BugIntegrationContext
{
    [Fact]
    public void maps_the_member_to_mt_deleted()
    {
        var mapping = DocumentMapping.For<Bug6194SoftDeletedDoc>();

        mapping.Metadata.IsSoftDeleted.Member.ShouldNotBeNull().Name.ShouldBe(nameof(Bug6194SoftDeletedDoc.Deleted));
    }

    [Fact]
    public async Task fills_the_member_on_load()
    {
        StoreOptions(_ => { });

        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Bug6194SoftDeletedDoc));

        var doc = new Bug6194SoftDeletedDoc();
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        theSession.Delete(doc);
        await theSession.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        var loaded = await query.Query<Bug6194SoftDeletedDoc>()
            .SingleAsync(x => x.Id == doc.Id && x.IsDeleted());

        loaded.Deleted.ShouldBeTrue();
    }
}
