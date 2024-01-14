using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Metadata;

public class MetadataTarget
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    public string CausationId { get; set; }
    public string CorrelationId { get; set; }
    public string LastModifiedBy { get; set; }

    public Guid Version { get; set; }

    public Dictionary<string, object> Headers { get; set; }
    public DateTimeOffset LastModified { get; set; }
}


public class when_using_the_user_defined_header_metadata: FlexibleDocumentMetadataContext
{
    public when_using_the_user_defined_header_metadata()
    {
    }

    protected override void MetadataIs(MartenRegistry.DocumentMappingExpression<MetadataTarget>.MetadataConfig metadata)
    {
        metadata.Headers.MapTo(x => x.Headers);
    }

    [Fact]
    public async Task save_and_load_and_see_header_values()
    {
        TheSession.SetHeader("name", "Jeremy");
        TheSession.SetHeader("hour", 5);

        var doc = new MetadataTarget();

        TheSession.Store(doc);
        await TheSession.SaveChangesAsync();

        await using var session = TheStore.QuerySession();

        var doc2 = await session.LoadAsync<MetadataTarget>(doc.Id);

        doc2.Headers["name"].ShouldBe("Jeremy");
        doc2.Headers["hour"].ShouldBe(5);
    }
}

public class when_mapping_to_the_version_and_others: FlexibleDocumentMetadataContext
{

    protected override void MetadataIs(MartenRegistry.DocumentMappingExpression<MetadataTarget>.MetadataConfig metadata)
    {
        metadata.Version.MapTo(x => x.Version);
        metadata.LastModified.MapTo(x => x.LastModified);
    }

    [Fact]
    public async Task version_is_available_on_query_only()
    {
        var doc = new MetadataTarget();
        TheSession.Store(doc);
        await TheSession.SaveChangesAsync();

        await using var query = TheStore.QuerySession();

        var doc2 = await query.LoadAsync<MetadataTarget>(doc.Id);
        doc2.Version.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task version_is_updated_on_the_document_when_it_is_saved()
    {
        var original = Guid.NewGuid();
        var doc = new MetadataTarget {Version = original};
        TheSession.Store(doc);
        await TheSession.SaveChangesAsync();

        doc.Version.ShouldNotBe(original);
    }

    [Fact]
    public async Task last_modified_is_updated_on_the_document_when_it_is_saved()
    {
        var original = Guid.NewGuid();
        var doc = new MetadataTarget {Version = original};
        TheSession.Store(doc);
        await TheSession.SaveChangesAsync();
    }
}

public class when_mapping_to_the_correlation_tracking : FlexibleDocumentMetadataContext
{
    protected override void MetadataIs(MartenRegistry.DocumentMappingExpression<MetadataTarget>.MetadataConfig metadata)
    {
        metadata.CorrelationId.MapTo(x => x.CorrelationId);
        metadata.CausationId.MapTo(x => x.CausationId);
        metadata.LastModifiedBy.MapTo(x => x.LastModifiedBy);
    }

    [Fact]
    public async Task save_and_load_metadata_causation()
    {
        var doc = new MetadataTarget();

        TheSession.Store(doc);
        await TheSession.SaveChangesAsync();

        var metadata = await TheSession.MetadataForAsync(doc);

        metadata.CausationId.ShouldBe(TheSession.CausationId);
        //metadata.CorrelationId.ShouldBe(TheSession.CorrelationId);
        //metadata.LastModifiedBy.ShouldBe(TheSession.LastModifiedBy);

        await using (var session2 = TheStore.QuerySession())
        {
            var doc2 = await session2.LoadAsync<MetadataTarget>(doc.Id);
            doc2.CausationId.ShouldBe(TheSession.CausationId);
            //doc2.CorrelationId.ShouldBe(TheSession.CorrelationId);
            //doc2.LastModifiedBy.ShouldBe(TheSession.LastModifiedBy);
        }

    }

    [Fact]
    public async Task save_and_load_metadata_correlation()
    {
        var doc = new MetadataTarget();

        TheSession.Store(doc);
        await TheSession.SaveChangesAsync();

        var metadata = await TheSession.MetadataForAsync(doc);

        metadata.CorrelationId.ShouldBe(TheSession.CorrelationId);

        await using (var session2 = TheStore.QuerySession())
        {
            var doc2 = await session2.LoadAsync<MetadataTarget>(doc.Id);
            doc2.CorrelationId.ShouldBe(TheSession.CorrelationId);
        }

    }

    [Fact]
    public async Task save_and_load_metadata_last_modified_by()
    {
        var doc = new MetadataTarget();

        TheSession.Store(doc);
        await TheSession.SaveChangesAsync();

        var metadata = await TheSession.MetadataForAsync(doc);

        metadata.LastModifiedBy.ShouldBe(TheSession.LastModifiedBy);

        await using (var session2 = TheStore.QuerySession())
        {
            var doc2 = await session2.LoadAsync<MetadataTarget>(doc.Id);
            doc2.LastModifiedBy.ShouldBe(TheSession.LastModifiedBy);
        }

    }
}

public class when_turning_off_all_optional_metadata: FlexibleDocumentMetadataContext
{
    protected override void MetadataIs(MartenRegistry.DocumentMappingExpression<MetadataTarget>.MetadataConfig metadata)
    {
        metadata.DisableInformationalFields();
    }
}

[Collection("metadata")]
public abstract class FlexibleDocumentMetadataContext : OneOffConfigurationsContext
{
    protected FlexibleDocumentMetadataContext() : base("metadata")
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<MetadataTarget>()
                .Metadata(MetadataIs);
        });

        TheSession.CorrelationId = "The Correlation";
        TheSession.CausationId = "The Cause";
        TheSession.LastModifiedBy = "Last Person";
    }

    protected abstract void MetadataIs(MartenRegistry.DocumentMappingExpression<MetadataTarget>.MetadataConfig metadata);

    [Fact]
    public void can_bulk_insert()
    {
        var docs = new MetadataTarget[]
        {
            new MetadataTarget(),
            new MetadataTarget(),
            new MetadataTarget(),
            new MetadataTarget(),
            new MetadataTarget(),
            new MetadataTarget()
        };

        TheStore.BulkInsert(docs);
    }

    [Fact]
    public async Task can_bulk_insert_async()
    {
        var docs = new MetadataTarget[]
        {
            new MetadataTarget(),
            new MetadataTarget(),
            new MetadataTarget(),
            new MetadataTarget(),
            new MetadataTarget(),
            new MetadataTarget()
        };

        await TheStore.BulkInsertAsync(docs);
    }

    [Fact]
    public async Task can_save_and_load()
    {
        var doc = new MetadataTarget();
        TheSession.Store(doc);
        await TheSession.SaveChangesAsync();

        await using var session = TheStore.LightweightSession();
        var doc2 = await session.LoadAsync<MetadataTarget>(doc.Id);
        doc2.ShouldNotBeNull();
    }

    [Fact]
    public async Task can_insert_and_load()
    {
        var doc = new MetadataTarget();
        TheSession.Insert(doc);
        await TheSession.SaveChangesAsync();

        await using var session = TheStore.LightweightSession();
        var doc2 = await session.LoadAsync<MetadataTarget>(doc.Id);
        doc2.ShouldNotBeNull();
    }

    [Fact]
    public async Task can_save_update_and_load_lightweight()
    {
        var doc = new MetadataTarget();
        TheSession.Store(doc);
        await TheSession.SaveChangesAsync();

        doc.Name = "different";
        TheSession.Update(doc);
        await TheSession.SaveChangesAsync();

        await using var session = TheStore.LightweightSession();
        var doc2 = await session.LoadAsync<MetadataTarget>(doc.Id);
        doc2.Name.ShouldBe("different");
    }

    [Fact]
    public async Task can_save_update_and_load_queryonly()
    {
        var doc = new MetadataTarget();
        TheSession.Store(doc);
        await TheSession.SaveChangesAsync();

        doc.Name = "different";
        TheSession.Update(doc);
        await TheSession.SaveChangesAsync();

        await using var session = TheStore.QuerySession();
        var doc2 = await session.LoadAsync<MetadataTarget>(doc.Id);
        doc2.Name.ShouldBe("different");
    }

    [Fact]
    public async Task can_save_update_and_load_with_identity_map()
    {
        await using var session = TheStore.IdentitySession();

        var doc = new MetadataTarget();
        session.Store(doc);
        await session.SaveChangesAsync();

        doc.Name = "different";
        session.Update(doc);
        await session.SaveChangesAsync();

        await using var session2 = TheStore.IdentitySession();
        var doc2 = await session2.LoadAsync<MetadataTarget>(doc.Id);
        doc2.Name.ShouldBe("different");
    }

    [Fact]
    public async Task can_save_update_and_load_with_dirty_session()
    {
        var doc = new MetadataTarget();
        await using var session = TheStore.DirtyTrackedSession();
        session.Store(doc);
        await session.SaveChangesAsync();

        doc.Name = "different";
        TheSession.Update(doc);
        await TheSession.SaveChangesAsync();

        await using var session2 = TheStore.DirtyTrackedSession();
        var doc2 = await session2.LoadAsync<MetadataTarget>(doc.Id);
        doc2.Name.ShouldBe("different");
    }
}

public class when_disabling_informational_schema_everywhere: OneOffConfigurationsContext
{
    public when_disabling_informational_schema_everywhere()
    {
        StoreOptions(opts =>
        {
            opts.Policies.DisableInformationalFields();
            opts.Schema.For<Target>().UseOptimisticConcurrency(true);
        });
    }

    [Fact]
    public void typical_documents_do_not_have_metadata()
    {
        var users = TheStore.Options.Storage.MappingFor(typeof(User));

        users.Metadata.Version.Enabled.ShouldBeFalse();
        users.Metadata.DotNetType.Enabled.ShouldBeFalse();
        users.Metadata.LastModified.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void explicit_rules_on_specific_documents_win()
    {
        var targets = TheStore.Options.Storage.MappingFor(typeof(Target));

        targets.Metadata.Version.Enabled.ShouldBeTrue();
        targets.Metadata.DotNetType.Enabled.ShouldBeFalse();
        targets.Metadata.LastModified.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void marker_interfaces_win()
    {
        var mapping = TheStore.Options.Storage.MappingFor(typeof(MyVersionedDoc));

        mapping.Metadata.Version.Enabled.ShouldBeTrue();
        mapping.Metadata.DotNetType.Enabled.ShouldBeFalse();
        mapping.Metadata.LastModified.Enabled.ShouldBeFalse();
    }


}
