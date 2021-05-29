using Marten.Schema;
using Marten.Linq.SoftDeletes;
using Shouldly;
using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Acceptance
{
    [Collection("metadata")]
    public class document_metadata_specs : OneOffConfigurationsContext
    {
        public document_metadata_specs() : base("metadata")
        {
        }

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

            theStore.Storage.MappingFor(typeof(DocWithMeta))
                .Metadata.Version.Member.Name.ShouldBe(nameof(DocWithMeta.Version));

            theStore.Storage.MappingFor(typeof(DocWithMeta))
                .Metadata.LastModified.Member.Name.ShouldBe(nameof(DocWithMeta.LastModified));

            theStore.Storage.MappingFor(typeof(DocWithMeta))
                .Metadata.IsSoftDeleted.Member.Name.ShouldBe(nameof(DocWithMeta.Deleted));

            theStore.Storage.MappingFor(typeof(DocWithMeta))
                .Metadata.SoftDeletedAt.Member.Name.ShouldBe(nameof(DocWithMeta.DeletedAt));

        }

        [Fact]
        public void set_the_metadata_projections_via_attributes()
        {
            theStore.Storage.MappingFor(typeof(DocWithAttributeMeta))
                .Metadata.Version.Member.Name.ShouldBe(nameof(DocWithAttributeMeta.Version));

            theStore.Storage.MappingFor(typeof(DocWithAttributeMeta))
                .Metadata.LastModified.Member.Name.ShouldBe(nameof(DocWithAttributeMeta.LastModified));

        }

        [Fact]
        public void doc_has_projected_data_after_storage()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>()
                    .Metadata(m => m.LastModified.MapTo(x => x.LastModified));
            });

            var doc = new DocWithMeta();

            using (var session = theStore.OpenSession())
            {
                session.Store(doc);
                session.MetadataFor(doc).ShouldBeNull();
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                var loaded = session.Load<DocWithMeta>(doc.Id);
                loaded.LastModified.ShouldNotBe(DateTimeOffset.MinValue);
            }
        }

        [Fact]
        public void doc_metadata_is_read_only_on_store()
        {
            var doc = new DocWithAttributeMeta();

            using (var session = theStore.OpenSession())
            {
                doc.LastModified = DateTime.UtcNow.AddYears(-1);
                doc.Version = Guid.Empty;
                session.Store(doc);
                session.MetadataFor(doc).ShouldBeNull();
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                var loaded = session.Load<DocWithAttributeMeta>(doc.Id);
                loaded.DocType.ShouldBeNull();
                loaded.TenantId.ShouldBeNull();
                (DateTime.UtcNow - loaded.LastModified.ToUniversalTime()).ShouldBeLessThan(TimeSpan.FromMinutes(1));
                loaded.Deleted.ShouldBeFalse();
                loaded.DeletedAt.ShouldBeNull();
                loaded.Version.ShouldNotBe(Guid.Empty);
            }
        }


        [Fact]
        public void doc_metadata_is_mapped_for_query_includes()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>().Metadata(m => m.LastModified.MapTo(x => x.LastModified));
            });

            var include = new IncludedDocWithMeta();
            var doc = new DocWithMeta();

            using (var session = theStore.OpenSession())
            {
                session.Store(include);
                doc.IncludedDocId = include.Id;
                session.Store(doc);
                session.MetadataFor(include).ShouldBeNull();
                session.MetadataFor(doc).ShouldBeNull();
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                IncludedDocWithMeta loadedInclude = null;
                var loaded = session.Query<DocWithMeta>().Include<IncludedDocWithMeta>(d => d.IncludedDocId, i => loadedInclude = i).Single(d => d.Id == doc.Id);

                loaded.ShouldNotBeNull();

                loadedInclude.ShouldNotBeNull();
                loadedInclude.Version.ShouldNotBe(Guid.Empty);
            }
        }

        [Fact]
        public void doc_metadata_is_updated_for_user_supplied_query()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>().Metadata(m => m.LastModified.MapTo(x => x.LastModified));
            });

            var doc = new DocWithMeta();
            DateTimeOffset lastMod = DateTime.UtcNow;

            using (var session = theStore.OpenSession())
            {
                session.Store(doc);
                session.MetadataFor(doc).ShouldBeNull();
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                var userQuery = session.Query<DocWithMeta>($"where data ->> 'Id' = '{doc.Id.ToString()}'").Single();
                userQuery.Description = "updated via a user SQL query";
                userQuery.LastModified.ShouldNotBe(DateTimeOffset.MinValue);
                lastMod = userQuery.LastModified;
                session.Store(userQuery);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                var userQuery = session.Query<DocWithMeta>($"where data ->> 'Id' = '{doc.Id.ToString()}'").Single();
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

            using (var session = theStore.OpenSession(tenant))
            {
                doc.LastModified = DateTime.UtcNow.AddYears(-1);
                session.Store(doc);
                (await session.MetadataForAsync(doc)).ShouldBeNull();
                await session.SaveChangesAsync();
            }

            using (var session = theStore.OpenSession(tenant))
            {
                session.Query<DocWithMeta>().Count(d => d.TenantId == tenant).ShouldBe(1);

                var loaded = await session.Query<DocWithMeta>().Where(d => d.Id == doc.Id).FirstOrDefaultAsync();
                loaded.TenantId.ShouldBe(tenant);
                loaded.LastModified.ShouldNotBe(DateTimeOffset.MinValue); // it's pretty well impossible to compare timestamps
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

            await theStore.BulkInsertAsync(tenant, new DocWithMeta[] { doc });

            using var session = theStore.OpenSession(tenant);
            session.Query<DocWithMeta>().Count(d => d.TenantId == tenant).ShouldBe(1);

            var loaded = await session.Query<DocWithMeta>().Where(d => d.Id == doc.Id).FirstOrDefaultAsync();
            loaded.TenantId.ShouldBe(tenant);
            (DateTime.UtcNow - loaded.LastModified.ToUniversalTime()).ShouldBeLessThan(TimeSpan.FromMinutes(1));
        }



        public void doc_metadata_is_mapped_for_doc_hierarchies()
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

            using (var session = theStore.OpenSession())
            {
                var doc = new DocWithMeta { Description = "transparent" };
                var red = new RedDocWithMeta { Description = "red doc" };
                var green = new GreenDocWithMeta { Description = "green doc" };
                var blue = new BlueDocWithMeta { Description = "blue doc" };
                var emerald = new EmeraldGreenDocWithMeta { Description = "emerald doc" };

                session.Store(doc, red, green, blue, emerald);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Query<DocWithMeta>().Count(d => d.DocType == "BASE").ShouldBe(1);
                session.Query<DocWithMeta>().Count(d => d.DocType == "blue_doc_with_meta").ShouldBe(1);
                session.Query<DocWithMeta>().Count(d => d.DocType == "red_doc_with_meta").ShouldBe(1);
                session.Query<DocWithMeta>().Count(d => d.DocType == "green_doc_with_meta").ShouldBe(1);
                session.Query<DocWithMeta>().Count(d => d.DocType == "emerald_green_doc_with_meta").ShouldBe(1);


                var redDocs = session.Query<RedDocWithMeta>().ToList();
                redDocs.Count.ShouldBe(1);
                redDocs.First().DocType.ShouldBe("red_doc_with_meta");
                session.Delete(redDocs.First());

                session.SaveChanges();
            }

        }


        [Fact]
        public void versions_are_assigned_during_bulk_inserts_as_field()
        {
            var docs = new AttVersionedDoc[100];
            for (var i = 0; i < docs.Length; i++)
            {
                docs[i] = new AttVersionedDoc();
            }

            theStore.BulkInsert(docs);

            foreach (var doc in docs)
            {
                doc.Version.ShouldNotBe(Guid.Empty);
            }

            using (var session = theStore.OpenSession())
            {
                session.Query<AttVersionedDoc>().Count(d => d.Version != Guid.Empty).ShouldBe(100);
            }
        }

        [Fact]
        public void versions_are_assigned_during_bulk_inserts_as_prop()
        {
            var docs = new PropVersionedDoc[100];
            for (int i = 0; i < docs.Length; i++)
            {
                docs[i] = new PropVersionedDoc();
            }

            theStore.BulkInsert(docs);

            foreach (var doc in docs)
            {
                doc.Version.ShouldNotBe(Guid.Empty);
            }

            using (var session = theStore.OpenSession())
            {
                session.Query<PropVersionedDoc>().Count(d => d.Version != Guid.Empty).ShouldBe(100);
            }
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

namespace Marten.Testing.Acceptance.StructuralTypes
{
    [StructuralTyped]
    public class DocWithMeta
    {
        public Guid Id { get; set; }
        public DateTime LastModified { get; set; }
        public string DocType { get; private set; }
    }
}
