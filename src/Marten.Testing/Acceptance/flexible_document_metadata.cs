using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{

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

    [Collection("metadata")]
    public class when_using_the_user_defined_header_metadata: FlexibleDocumentMetadataContext
    {
        public when_using_the_user_defined_header_metadata() : base("metadata")
        {
        }

        protected override void MetadataIs(MartenRegistry.DocumentMappingExpression<MetadataTarget>.MetadataConfig metadata)
        {
            metadata.Headers.MapTo(x => x.Headers);
        }

        [Fact]
        public async Task save_and_load_and_see_header_values()
        {
            theSession.SetHeader("name", "Jeremy");
            theSession.SetHeader("hour", 5);

            var doc = new MetadataTarget();

            theSession.Store(doc);
            await theSession.SaveChangesAsync();

            using var session = theStore.QuerySession();

            var doc2 = await session.LoadAsync<MetadataTarget>(doc.Id);

            doc2.Headers["name"].ShouldBe("Jeremy");
            doc2.Headers["hour"].ShouldBe(5);
        }
    }

    [Collection("metadata")]
    public class when_mapping_to_the_version_and_others: FlexibleDocumentMetadataContext
    {
        public when_mapping_to_the_version_and_others() : base("metadata")
        {

        }

        protected override void MetadataIs(MartenRegistry.DocumentMappingExpression<MetadataTarget>.MetadataConfig metadata)
        {
            metadata.Version.MapTo(x => x.Version);
            metadata.LastModified.MapTo(x => x.LastModified);
        }

        [Fact]
        public async Task version_is_available_on_query_only()
        {
            var doc = new MetadataTarget();
            theSession.Store(doc);
            await theSession.SaveChangesAsync();

            using var query = theStore.QuerySession();

            var doc2 = await query.LoadAsync<MetadataTarget>(doc.Id);
            doc2.Version.ShouldNotBe(Guid.Empty);
        }

        [Fact]
        public async Task version_is_updated_on_the_document_when_it_is_saved()
        {
            var original = Guid.NewGuid();
            var doc = new MetadataTarget {Version = original};
            theSession.Store(doc);
            await theSession.SaveChangesAsync();

            doc.Version.ShouldNotBe(original);
        }

        [Fact]
        public async Task last_modified_is_updated_on_the_document_when_it_is_saved()
        {
            var original = Guid.NewGuid();
            var doc = new MetadataTarget {Version = original};
            theSession.Store(doc);
            await theSession.SaveChangesAsync();
        }
    }

    [Collection("metadata")]
    public class when_mapping_to_the_correlation_tracking : FlexibleDocumentMetadataContext
    {
        public when_mapping_to_the_correlation_tracking() : base("metadata")
        {
        }

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

            theSession.Store(doc);
            await theSession.SaveChangesAsync();

            var metadata = await theSession.MetadataForAsync(doc);

            metadata.CausationId.ShouldBe(theSession.CausationId);
            //metadata.CorrelationId.ShouldBe(theSession.CorrelationId);
            //metadata.LastModifiedBy.ShouldBe(theSession.LastModifiedBy);

            using (var session2 = theStore.QuerySession())
            {
                var doc2 = await session2.LoadAsync<MetadataTarget>(doc.Id);
                doc2.CausationId.ShouldBe(theSession.CausationId);
                //doc2.CorrelationId.ShouldBe(theSession.CorrelationId);
                //doc2.LastModifiedBy.ShouldBe(theSession.LastModifiedBy);
            }

        }

        [Fact]
        public async Task save_and_load_metadata_correlation()
        {
            var doc = new MetadataTarget();

            theSession.Store(doc);
            await theSession.SaveChangesAsync();

            var metadata = await theSession.MetadataForAsync(doc);

            metadata.CorrelationId.ShouldBe(theSession.CorrelationId);

            using (var session2 = theStore.QuerySession())
            {
                var doc2 = await session2.LoadAsync<MetadataTarget>(doc.Id);
                doc2.CorrelationId.ShouldBe(theSession.CorrelationId);
            }

        }

        [Fact]
        public async Task save_and_load_metadata_last_modified_by()
        {
            var doc = new MetadataTarget();

            theSession.Store(doc);
            await theSession.SaveChangesAsync();

            var metadata = await theSession.MetadataForAsync(doc);

            metadata.LastModifiedBy.ShouldBe(theSession.LastModifiedBy);

            using (var session2 = theStore.QuerySession())
            {
                var doc2 = await session2.LoadAsync<MetadataTarget>(doc.Id);
                doc2.LastModifiedBy.ShouldBe(theSession.LastModifiedBy);
            }

        }
    }

    [Collection("metadata")]
    public class when_turning_off_all_optional_metadata: FlexibleDocumentMetadataContext
    {
        public when_turning_off_all_optional_metadata() : base("metadata")
        {
        }

        protected override void MetadataIs(MartenRegistry.DocumentMappingExpression<MetadataTarget>.MetadataConfig metadata)
        {
            metadata.DisableInformationalFields();
        }
    }

    public abstract class FlexibleDocumentMetadataContext : OneOffConfigurationsContext
    {
        protected FlexibleDocumentMetadataContext(string schemaName) : base(schemaName)
        {
            StoreOptions(opts =>
            {
                opts.Schema.For<MetadataTarget>()
                    .Metadata(MetadataIs);
            });

            theSession.CorrelationId = "The Correlation";
            theSession.CausationId = "The Cause";
            theSession.LastModifiedBy = "Last Person";
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

            theStore.BulkInsert(docs);
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

            await theStore.BulkInsertAsync(docs);
        }

        [Fact]
        public async Task can_save_and_load()
        {
            var doc = new MetadataTarget();
            theSession.Store(doc);
            await theSession.SaveChangesAsync();

            using var session = theStore.LightweightSession();
            var doc2 = session.LoadAsync<MetadataTarget>(doc.Id);
            SpecificationExtensions.ShouldNotBeNull(doc2);
        }

        [Fact]
        public async Task can_insert_and_load()
        {
            var doc = new MetadataTarget();
            theSession.Insert(doc);
            await theSession.SaveChangesAsync();

            using var session = theStore.LightweightSession();
            var doc2 = await session.LoadAsync<MetadataTarget>(doc.Id);
            doc2.ShouldNotBeNull();
        }

        [Fact]
        public async Task can_save_update_and_load_lightweight()
        {
            var doc = new MetadataTarget();
            theSession.Store(doc);
            await theSession.SaveChangesAsync();

            doc.Name = "different";
            theSession.Update(doc);
            await theSession.SaveChangesAsync();

            using var session = theStore.LightweightSession();
            var doc2 = await session.LoadAsync<MetadataTarget>(doc.Id);
            doc2.Name.ShouldBe("different");
        }

        [Fact]
        public async Task can_save_update_and_load_queryonly()
        {
            var doc = new MetadataTarget();
            theSession.Store(doc);
            await theSession.SaveChangesAsync();

            doc.Name = "different";
            theSession.Update(doc);
            await theSession.SaveChangesAsync();

            using var session = theStore.QuerySession();
            var doc2 = await session.LoadAsync<MetadataTarget>(doc.Id);
            doc2.Name.ShouldBe("different");
        }

        [Fact]
        public async Task can_save_update_and_load_with_identity_map()
        {
            using var session = theStore.OpenSession();

            var doc = new MetadataTarget();
            session.Store(doc);
            await session.SaveChangesAsync();

            doc.Name = "different";
            session.Update(doc);
            await session.SaveChangesAsync();

            using var session2 = theStore.OpenSession();
            var doc2 = await session2.LoadAsync<MetadataTarget>(doc.Id);
            doc2.Name.ShouldBe("different");
        }

        [Fact]
        public async Task can_save_update_and_load_with_dirty_session()
        {
            var doc = new MetadataTarget();
            using var session = theStore.DirtyTrackedSession();
            session.Store(doc);
            await session.SaveChangesAsync();

            doc.Name = "different";
            theSession.Update(doc);
            await theSession.SaveChangesAsync();

            using var session2 = theStore.DirtyTrackedSession();
            var doc2 = await session2.LoadAsync<MetadataTarget>(doc.Id);
            doc2.Name.ShouldBe("different");
        }
    }

    [Collection("metadata")]
    public class when_disabling_informational_schema_everywhere: OneOffConfigurationsContext
    {
        public when_disabling_informational_schema_everywhere() : base("metadata")
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
            var users = theStore.Options.Storage.MappingFor(typeof(User));

            users.Metadata.Version.Enabled.ShouldBeFalse();
            users.Metadata.DotNetType.Enabled.ShouldBeFalse();
            users.Metadata.LastModified.Enabled.ShouldBeFalse();
        }

        [Fact]
        public void explicit_rules_on_specific_documents_win()
        {
            var targets = theStore.Options.Storage.MappingFor(typeof(Target));

            targets.Metadata.Version.Enabled.ShouldBeTrue();
            targets.Metadata.DotNetType.Enabled.ShouldBeFalse();
            targets.Metadata.LastModified.Enabled.ShouldBeFalse();
        }

        [Fact]
        public void marker_interfaces_win()
        {
            var mapping = theStore.Options.Storage.MappingFor(typeof(MyVersionedDoc));

            mapping.Metadata.Version.Enabled.ShouldBeTrue();
            mapping.Metadata.DotNetType.Enabled.ShouldBeFalse();
            mapping.Metadata.LastModified.Enabled.ShouldBeFalse();
        }


    }
}
