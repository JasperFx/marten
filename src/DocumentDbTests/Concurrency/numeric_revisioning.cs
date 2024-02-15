using System;
using System.Threading.Tasks;
using Castle.Components.DictionaryAdapter;
using Marten.Exceptions;
using Marten.Metadata;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.Concurrency;

public class numeric_revisioning: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public numeric_revisioning(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void use_numeric_revisions_is_off_by_default()
    {
        var mapping = (DocumentMapping)theStore.Options.Storage.FindMapping(typeof(Target));
        mapping.UseNumericRevisions.ShouldBeFalse();
        mapping.Metadata.Revision.Enabled.ShouldBeFalse();
    }


    [Fact]
    public void using_fluent_interface()
    {
        StoreOptions(opts => opts.Schema.For<Target>().UseNumericRevisions(true));

        var mapping = (DocumentMapping)theStore.Options.Storage.FindMapping(typeof(Target));
        mapping.Metadata.Revision.Enabled.ShouldBeTrue();
        mapping.UseNumericRevisions.ShouldBeTrue();
    }

    [Fact]
    public void decorate_int_property_with_Version_attribute()
    {
        var mapping = (DocumentMapping)theStore.Options.Storage.FindMapping(typeof(OtherRevisionedDoc));
        mapping.Metadata.Revision.Enabled.ShouldBeTrue();
        mapping.UseNumericRevisions.ShouldBeTrue();
        mapping.Metadata.Revision.Member.Name.ShouldBe("Version");
    }

    [Fact]
    public void infer_configuration_from_IRevisioned_interface()
    {
        var mapping = (DocumentMapping)theStore.Options.Storage.FindMapping(typeof(RevisionedDoc));
        mapping.Metadata.Revision.Enabled.ShouldBeTrue();
        mapping.UseNumericRevisions.ShouldBeTrue();
        mapping.Metadata.Revision.Member.Name.ShouldBe("Version");
    }

    [Fact]
    public async Task happy_path_insert()
    {
        var doc = new RevisionedDoc { Name = "Tim" };
        theSession.Insert(doc);
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<RevisionedDoc>(doc.Id);
        loaded.Version.ShouldBe(1);

        doc.Version.ShouldBe(1);
    }

    [Fact]
    public async Task fetch_document_metadata()
    {
        var doc = new RevisionedDoc { Name = "Tim" };
        theSession.Insert(doc);
        await theSession.SaveChangesAsync();

        var metadata = await theSession.MetadataForAsync(doc);
        metadata.CurrentRevision.ShouldBe(1);
    }

    [Fact]
    public async Task bulk_inserts()
    {
        var doc1 = new RevisionedDoc { Name = "Tim" };
        var doc2 = new RevisionedDoc { Name = "Molly" };
        var doc3 = new RevisionedDoc { Name = "JD" };

        await theStore.BulkInsertDocumentsAsync(new[] { doc1, doc2, doc3 });

        (await theSession.MetadataForAsync(doc1)).CurrentRevision.ShouldBe(1);
        (await theSession.MetadataForAsync(doc2)).CurrentRevision.ShouldBe(1);
        (await theSession.MetadataForAsync(doc3)).CurrentRevision.ShouldBe(1);

        (await theSession.LoadAsync<RevisionedDoc>(doc1.Id)).ShouldNotBeNull();
        (await theSession.LoadAsync<RevisionedDoc>(doc2.Id)).ShouldNotBeNull();
        (await theSession.LoadAsync<RevisionedDoc>(doc3.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task store_with_no_revision_from_start_succeeds_with_revision_1()
    {
        var doc1 = new RevisionedDoc { Name = "Tim" };
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        (await theSession.MetadataForAsync(doc1)).CurrentRevision.ShouldBe(1);
        (await theSession.LoadAsync<RevisionedDoc>(doc1.Id)).Version.ShouldBe(1);
    }

    [Fact]
    public async Task store_twice_with_no_version_can_override()
    {
        var doc1 = new RevisionedDoc { Name = "Tim" };
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();


        theSession.Logger = new TestOutputMartenLogger(_output);
        theSession.Store(new RevisionedDoc{Id = doc1.Id, Name = "Brad"});
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<RevisionedDoc>(doc1.Id)).Name.ShouldBe("Brad");
    }

    [Fact]
    public async Task optimistic_concurrency_failure()
    {
        var doc1 = new RevisionedDoc { Name = "Tim" };
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Bill";
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        doc1.Name = "Dru";
        theSession.Store(doc1);
        await theSession.SaveChangesAsync();

        var doc2 = new RevisionedDoc { Id = doc1.Id, Name = "Wrong" };
        theSession.UpdateRevision(doc2, doc1.Version + 1);
        await theSession.SaveChangesAsync();

        await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            theSession.UpdateRevision(doc2, 2);
            await theSession.SaveChangesAsync();
        });
    }
}



public class RevisionedDoc: IRevisioned
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    public int Version { get; set; }
}

public class OtherRevisionedDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    [Version]
    public int Version { get; set; }
}
