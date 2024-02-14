using System;
using System.Threading.Tasks;
using Castle.Components.DictionaryAdapter;
using Marten.Metadata;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Concurrency;

public class numeric_revisioning: OneOffConfigurationsContext
{
    public numeric_revisioning()
    {
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
