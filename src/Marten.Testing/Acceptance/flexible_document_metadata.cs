using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{

    public class MetadataTarget
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
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
            var doc2 = session.LoadAsync<MetadataTarget>(doc.Id);
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
}
