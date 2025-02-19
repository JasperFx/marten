using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Metadata
{
    public class document_metadata_specs : OneOffConfigurationsContext
    {

        [Fact]
        public void set_the_metadata_projections_through_the_fluent_interface()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<DocWithMeta>().Metadata(m =>
                    {
                        m.Version.MapTo(x => x.Version);
                        m.LastModified.MapTo(x => x.LastModified);
                        m.IsSoftDeleted.MapTo(x => x.Deleted);
                        m.SoftDeletedAt.MapTo(x => x.DeletedAt);
                    })
                    .SoftDeleted();
            });

            theStore.StorageFeatures.MappingFor(typeof(DocWithMeta))
                .Metadata.Version.Member.Name.ShouldBe(nameof(DocWithMeta.Version));

            theStore.StorageFeatures.MappingFor(typeof(DocWithMeta))
                .Metadata.LastModified.Member.Name.ShouldBe(nameof(DocWithMeta.LastModified));

            theStore.StorageFeatures.MappingFor(typeof(DocWithMeta))
                .Metadata.IsSoftDeleted.Member.Name.ShouldBe(nameof(DocWithMeta.Deleted));

            theStore.StorageFeatures.MappingFor(typeof(DocWithMeta))
                .Metadata.SoftDeletedAt.Member.Name.ShouldBe(nameof(DocWithMeta.DeletedAt));

        }

        [Fact]
        public void set_the_metadata_projections_via_attributes()
        {
            theStore.StorageFeatures.MappingFor(typeof(DocWithAttributeMeta))
                .Metadata.Version.Member.Name.ShouldBe(nameof(DocWithAttributeMeta.Version));

            theStore.StorageFeatures.MappingFor(typeof(DocWithAttributeMeta))
                .Metadata.LastModified.Member.Name.ShouldBe(nameof(DocWithAttributeMeta.LastModified));

        }

        [Fact]
        public async Task doc_has_projected_data_after_storage()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>()
                    .Metadata(m => m.LastModified.MapTo(x => x.LastModified));
            });

            var doc = new DocWithMeta();

            using (var session = theStore.LightweightSession())
            {
                session.Store(doc);
                (await session.MetadataForAsync(doc)).ShouldBeNull();
                await session.SaveChangesAsync();
            }

            using (var session = theStore.LightweightSession())
            {
                var loaded = await session.LoadAsync<DocWithMeta>(doc.Id);
                loaded.LastModified.ShouldNotBe(DateTimeOffset.MinValue);
            }
        }

        [Fact]
        public async Task doc_metadata_is_read_only_on_store()
        {
            var doc = new DocWithAttributeMeta();

            using (var session = theStore.LightweightSession())
            {
                doc.LastModified = DateTime.UtcNow.AddYears(-1);
                doc.Version = Guid.Empty;
                session.Store(doc);
                (await session.MetadataForAsync(doc)).ShouldBeNull();
                await session.SaveChangesAsync();
            }

            using (var session = theStore.LightweightSession())
            {
                var loaded = await session.LoadAsync<DocWithAttributeMeta>(doc.Id);
                loaded.DocType.ShouldBeNull();
                loaded.TenantId.ShouldBeNull();
                (DateTime.UtcNow - loaded.LastModified.ToUniversalTime()).ShouldBeLessThan(TimeSpan.FromMinutes(1));
                loaded.Deleted.ShouldBeFalse();
                loaded.DeletedAt.ShouldBeNull();
                loaded.Version.ShouldNotBe(Guid.Empty);
            }
        }


        [Fact]
        public async Task doc_metadata_is_mapped_for_query_includes()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>().Metadata(m => m.LastModified.MapTo(x => x.LastModified));
            });

            var include = new IncludedDocWithMeta();
            var doc = new DocWithMeta();

            using (var session = theStore.LightweightSession())
            {
                session.Store(include);
                doc.IncludedDocId = include.Id;
                session.Store(doc);
                (await session.MetadataForAsync(include)).ShouldBeNull();
                (await session.MetadataForAsync(doc)).ShouldBeNull();
                await session.SaveChangesAsync();
            }

            using (var session = theStore.LightweightSession())
            {
                IncludedDocWithMeta loadedInclude = null;
                var loaded = session.Query<DocWithMeta>().Include<IncludedDocWithMeta>(d => d.IncludedDocId, i => loadedInclude = i).Single(d => d.Id == doc.Id);

                loaded.ShouldNotBeNull();

                loadedInclude.ShouldNotBeNull();
                loadedInclude.Version.ShouldNotBe(Guid.Empty);
            }
        }

        [Fact]
        public async Task doc_metadata_is_updated_for_user_supplied_query()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>().Metadata(m => m.LastModified.MapTo(x => x.LastModified));
            });

            var doc = new DocWithMeta();
            DateTimeOffset lastMod = DateTime.UtcNow;

            using (var session = theStore.LightweightSession())
            {
                session.Store(doc);
                (await session.MetadataForAsync(doc)).ShouldBeNull();
                await session.SaveChangesAsync();
            }

            using (var session = theStore.LightweightSession())
            {
                var userQuery = (await session.QueryAsync<DocWithMeta>($"where data ->> 'Id' = '{doc.Id.ToString()}'")).Single();
                userQuery.Description = "updated via a user SQL query";
                ShouldBeTestExtensions.ShouldNotBe(userQuery.LastModified, DateTimeOffset.MinValue);
                lastMod = userQuery.LastModified;
                session.Store(userQuery);
                await session.SaveChangesAsync();
            }

            using (var session = theStore.LightweightSession())
            {
                var userQuery = (await session.QueryAsync<DocWithMeta>($"where data ->> 'Id' = '{doc.Id.ToString()}'")).Single();
                userQuery.LastModified.ShouldBeGreaterThanOrEqualTo(lastMod);
            }
        }

        [Fact]
        public async Task doc_metadata_is_mapped_for_conjoined_tenant()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>()
                    .MultiTenanted()
                    .Metadata(m =>
                    {
                        m.TenantId.MapTo(x => x.TenantId);
                        m.LastModified.MapTo(x => x.LastModified);
                    });
            });

            var doc = new DocWithMeta();
            var tenant = "TENANT_A";

            await using (var session = theStore.LightweightSession(tenant))
            {
                doc.LastModified = DateTime.UtcNow.AddYears(-1);
                session.Store(doc);
                (await session.MetadataForAsync(doc)).ShouldBeNull();
                await session.SaveChangesAsync();
            }

            await using (var session = theStore.LightweightSession(tenant))
            {
                session.Query<DocWithMeta>().Count(d => d.TenantId == tenant).ShouldBe(1);

                var loaded = await session.Query<DocWithMeta>().Where(d => d.Id == doc.Id).FirstOrDefaultAsync();
                ShouldBeStringTestExtensions.ShouldBe(loaded.TenantId, tenant);
                ShouldBeTestExtensions.ShouldNotBe(loaded.LastModified, DateTimeOffset.MinValue); // it's pretty well impossible to compare timestamps
            }
        }

        [Fact]
        public async Task doc_metadata_is_mapped_for_bulk_inserted_conjoined_tenant()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>()
                    .MultiTenanted()
                    .Metadata(m =>
                    {
                        m.TenantId.MapTo(x => x.TenantId);
                        m.LastModified.MapTo(x => x.LastModified);
                    });
            });

            var doc = new DocWithMeta();
            var tenant = "TENANT_A";

            await theStore.BulkInsertAsync(tenant, new[] { doc });

            await using var session = theStore.QuerySession(tenant);
            session.Query<DocWithMeta>().Count(d => d.TenantId == tenant).ShouldBe(1);

            var loaded = await session.Query<DocWithMeta>().Where(d => d.Id == doc.Id).FirstOrDefaultAsync();
            loaded.TenantId.ShouldBe(tenant);
            (DateTime.UtcNow - loaded.LastModified.ToUniversalTime()).ShouldBeLessThan<TimeSpan>(TimeSpan.FromMinutes(1));
        }

        public async Task doc_metadata_is_mapped_for_doc_hierarchies()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>()
                    .AddSubClassHierarchy(typeof(RedDocWithMeta), typeof(BlueDocWithMeta), typeof(GreenDocWithMeta),
                        typeof(EmeraldGreenDocWithMeta))
                    .SoftDeleted()
                    .Metadata(m =>
                    {
                        m.IsSoftDeleted.MapTo(x => x.Deleted);
                        m.SoftDeletedAt.MapTo(x => x.DeletedAt);
                        m.DocumentType.MapTo(x => x.DocType);
                    });
            });

            using (var session = theStore.LightweightSession())
            {
                var doc = new DocWithMeta { Description = "transparent" };
                var red = new RedDocWithMeta { Description = "red doc" };
                var green = new GreenDocWithMeta { Description = "green doc" };
                var blue = new BlueDocWithMeta { Description = "blue doc" };
                var emerald = new EmeraldGreenDocWithMeta { Description = "emerald doc" };

                session.Store(doc, red, green, blue, emerald);
                await session.SaveChangesAsync();
            }

            using (var session = theStore.IdentitySession())
            {
                session.Query<DocWithMeta>().Count(d => d.DocType == "BASE").ShouldBe(1);
                session.Query<DocWithMeta>().Count(d => d.DocType == "blue_doc_with_meta").ShouldBe(1);
                session.Query<DocWithMeta>().Count(d => d.DocType == "red_doc_with_meta").ShouldBe(1);
                session.Query<DocWithMeta>().Count(d => d.DocType == "green_doc_with_meta").ShouldBe(1);
                session.Query<DocWithMeta>().Count(d => d.DocType == "emerald_green_doc_with_meta").ShouldBe(1);


                var redDocs = session.Query<RedDocWithMeta>().ToList();
                redDocs.Count.ShouldBe(1);
                ShouldBeStringTestExtensions.ShouldBe(redDocs.First().DocType, "red_doc_with_meta");
                session.Delete(redDocs.First());

                await session.SaveChangesAsync();
            }
        }

        [Fact]
        public async Task versions_are_assigned_during_bulk_inserts_as_field()
        {
            var docs = new AttVersionedDoc[100];
            for (var i = 0; i < docs.Length; i++)
            {
                docs[i] = new AttVersionedDoc();
            }

            await theStore.BulkInsertAsync(docs);

            foreach (var doc in docs)
            {
                doc.Version.ShouldNotBe(Guid.Empty);
            }

            using var session = theStore.QuerySession();
            session.Query<AttVersionedDoc>().Count(d => d.Version != Guid.Empty).ShouldBe(100);
        }

        [Fact]
        public async Task versions_are_assigned_during_bulk_inserts_as_prop()
        {
            var docs = new PropVersionedDoc[100];
            for (int i = 0; i < docs.Length; i++)
            {
                docs[i] = new PropVersionedDoc();
            }

            await theStore.BulkInsertAsync(docs);

            foreach (var doc in docs)
            {
                doc.Version.ShouldNotBe(Guid.Empty);
            }

            using var session = theStore.QuerySession();
            session.Query<PropVersionedDoc>().Count(d => d.Version != Guid.Empty).ShouldBe(100);
        }
    }

    public class DocWithMeta
    {
        public Guid Id { get; set; }
        public string Description { get; set; }
        public Guid Version { get; set; }
        public DateTimeOffset LastModified { get; set; }
        public string TenantId { get; private set; }
        public bool Deleted { get; private set; }
        public DateTimeOffset? DeletedAt { get; private set; }
        public string DocType { get; private set; }
        public Guid IncludedDocId { get; set; }
    }

    internal class RedDocWithMeta: DocWithMeta
    {
        public int RedHue { get; set; }
    }

    internal class BlueDocWithMeta: DocWithMeta
    {
        public int BlueHue { get; set; }
    }

    internal class GreenDocWithMeta: DocWithMeta
    {
        public int GreenHue { get; set; }
    }

    internal class EmeraldGreenDocWithMeta: GreenDocWithMeta
    {
        public string Label { get; set; }
    }

    public class IncludedDocWithMeta
    {
        public Guid Id { get; set; }
        [Version]
        public Guid Version { get; private set; }
        public DateTimeOffset LastModified { get; private set; }

    }

    public class DocWithAttributeMeta
    {
        public Guid Id { get; set; }
        public string TenantId { get; private set; }
        public string DocType { get; private set; }
        public bool Deleted { get; private set; }
        public DateTime? DeletedAt { get; private set; }
        [Version]
        public Guid Version { get; set; }
        [LastModifiedMetadata]
        public DateTimeOffset LastModified { get; set; }

    }


}

namespace other
{
    [StructuralTyped]
    public class DocWithMeta
    {
        public Guid Id { get; set; }
        public DateTime LastModified { get; set; }
        public string DocType { get; private set; }
    }
}

