using Marten.Schema;
using Marten.Linq.SoftDeletes;
using Shouldly;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class projecting_metadata_to_documents_schema_tests
    {
        [Fact]
        public void set_the_metadata_projections_through_the_fluent_interface()
        {
            using (var store = TestingDocumentStore.For(_ =>
            {
                _.Schema.For<DocWithMeta>().VersionedWith(x => x.Version);
                _.Schema.For<DocWithMeta>().MapLastModifiedTo(x => x.LastModified);
                _.Schema.For<DocWithMeta>().MapIsSoftDeletedTo(x => x.Deleted);
                _.Schema.For<DocWithMeta>().MapSoftDeletedAtTo(x => x.DeletedAt);
                _.Schema.For<DocWithMeta>().MapDotNetTypeTo(x => x.DotNetType);
                _.Schema.For<DocWithMeta>().SoftDeleted();
            }))
            {
                store.Storage.MappingFor(typeof(DocWithMeta))
                    .VersionMember.Name.ShouldBe(nameof(DocWithMeta.Version));

                store.Storage.MappingFor(typeof(DocWithMeta))
                    .LastModifiedMember.Name.ShouldBe(nameof(DocWithMeta.LastModified));

                store.Storage.MappingFor(typeof(DocWithMeta))
                    .IsSoftDeletedMember.Name.ShouldBe(nameof(DocWithMeta.Deleted));

                store.Storage.MappingFor(typeof(DocWithMeta))
                    .SoftDeletedAtMember.Name.ShouldBe(nameof(DocWithMeta.DeletedAt));

                store.Storage.MappingFor(typeof(DocWithMeta))
                    .DotNetTypeMember.Name.ShouldBe(nameof(DocWithMeta.DotNetType));
            }
        }

        [Fact]
        public void set_the_metadata_projections_via_attributes()
        {
            using (var store = TestingDocumentStore.For(_ =>
            {

            }))
            {
                store.Storage.MappingFor(typeof(DocWithAttributeMeta))
                    .VersionMember.Name.ShouldBe(nameof(DocWithAttributeMeta.Version));

                store.Storage.MappingFor(typeof(DocWithAttributeMeta))
                    .LastModifiedMember.Name.ShouldBe(nameof(DocWithAttributeMeta.LastModified));

                store.Storage.MappingFor(typeof(DocWithAttributeMeta))
                    .DotNetTypeMember.Name.ShouldBe(nameof(DocWithAttributeMeta.DotNetType));
            }

        }
    }

    public class projecting_metadata_end_to_end_tests: IntegratedFixture
    {
        [Fact]
        public void doc_has_projected_data_after_storage()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>()
                .MapDotNetTypeTo(x => x.DotNetType)
                .MapLastModifiedTo(x => x.LastModified);
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
                loaded.LastModified.ShouldNotBe(DateTime.MinValue);
                loaded.DotNetType.ShouldBe(typeof(DocWithMeta).FullName);
            }
        }

        [Fact]
        public void doc_metadata_is_read_only_on_store()
        {
            var doc = new DocWithAttributeMeta();

            using (var session = theStore.OpenSession())
            {
                doc.DotNetType = "my dotnet type";
                doc.LastModified = DateTime.UtcNow.AddYears(-1);
                doc.Version = Guid.Empty;
                session.Store(doc);
                session.MetadataFor(doc).ShouldBeNull();
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                var loaded = session.Load<DocWithAttributeMeta>(doc.Id);
                loaded.DotNetType.ShouldBe(typeof(DocWithAttributeMeta).FullName);
                loaded.DocType.ShouldBeNull();
                loaded.TenantId.ShouldBeNull();
                (DateTime.UtcNow - loaded.LastModified.ToUniversalTime()).ShouldBeLessThan(TimeSpan.FromMinutes(1));
                loaded.Deleted.ShouldBeFalse();
                loaded.DeletedAt.ShouldBeNull();
                loaded.Version.ShouldNotBe(Guid.Empty);
            }
        }

        [Fact]
        public void doc_metadata_is_read_only_on_update()
        {
            var doc = new DocWithAttributeMeta();

            using (var session = theStore.OpenSession())
            {
                session.Store(doc);
                session.MetadataFor(doc).ShouldBeNull();
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                var loaded = session.Load<DocWithAttributeMeta>(doc.Id);
                loaded.DotNetType = "my type, not yours";
                session.Store(loaded);
                Should.Throw<InvalidOperationException>(() => session.SaveChanges());
            }
        }

        [Fact]
        public void doc_metadata_is_mapped_for_query_includes()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>()
                .MapDotNetTypeTo(x => x.DotNetType)
                .MapLastModifiedTo(x => x.LastModified);
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
                loaded.DotNetType.ShouldBe(typeof(DocWithMeta).FullName);

                loadedInclude.ShouldNotBeNull();
                loadedInclude.DotNetType.ShouldBe(typeof(IncludedDocWithMeta).FullName);
                loadedInclude.Version.ShouldNotBe(Guid.Empty);
            }
        }

        [Fact]
        public void doc_metadata_is_updated_for_user_supplied_query()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>()
                .MapDotNetTypeTo(x => x.DotNetType)
                .MapLastModifiedTo(x => x.LastModified);
            });

            var doc = new DocWithMeta();
            var lastMod = DateTime.UtcNow;

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
                userQuery.LastModified.ShouldBeGreaterThanOrEqualTo(lastMod);
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
                .MapTenantIdTo(x => x.TenantId)
                .MapDotNetTypeTo(x => x.DotNetType)
                .MapLastModifiedTo(x => x.LastModified);
            });

            var doc = new DocWithMeta();
            var tenant = "TENANT_A";

            using (var session = theStore.OpenSession(tenant))
            {
                doc.DotNetType = "my dotnet type";
                doc.LastModified = DateTime.UtcNow.AddYears(-1);
                session.Store(doc);
                session.MetadataFor(doc).ShouldBeNull();
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession(tenant))
            {
                session.Query<DocWithMeta>().Where(d => d.TenantId == tenant).Count().ShouldBe(1);

                var loaded = await session.Query<DocWithMeta>().Where(d => d.Id == doc.Id).FirstOrDefaultAsync();
                loaded.DotNetType.ShouldBe(typeof(DocWithMeta).FullName);
                loaded.TenantId.ShouldBe(tenant);
                (DateTime.UtcNow - loaded.LastModified.ToUniversalTime()).ShouldBeLessThan(TimeSpan.FromMinutes(1));
            }
        }

        [Fact]
        public async Task doc_metadata_is_mapped_for_bulk_inserted_conjoined_tenant()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>()
                .MultiTenanted()
                .MapTenantIdTo(x => x.TenantId)
                .MapDotNetTypeTo(x => x.DotNetType)
                .MapLastModifiedTo(x => x.LastModified);
            });

            var doc = new DocWithMeta();
            var tenant = "TENANT_A";

            theStore.BulkInsert(tenant, new DocWithMeta[] { doc });

            using (var session = theStore.OpenSession(tenant))
            {
                session.Query<DocWithMeta>().Where(d => d.TenantId == tenant).Count().ShouldBe(1);

                var loaded = await session.Query<DocWithMeta>().Where(d => d.Id == doc.Id).FirstOrDefaultAsync();
                loaded.DotNetType.ShouldBe(typeof(DocWithMeta).FullName);
                loaded.TenantId.ShouldBe(tenant);
                (DateTime.UtcNow - loaded.LastModified.ToUniversalTime()).ShouldBeLessThan(TimeSpan.FromMinutes(1));
            }
        }

        [Fact]
        public void doc_metadata_is_mapped_for_structural_typed_docs()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>()
                .MapDotNetTypeTo(x => x.DotNetType)
                .MapLastModifiedTo(x => x.LastModified);
            });

            var bigDoc = new DocWithMeta();

            using (var session = theStore.OpenSession())
            {
                session.Store(bigDoc);
                session.SaveChanges();

                var slimDoc = session.Load<StructuralTypes.DocWithMeta>(bigDoc.Id);
                slimDoc.DotNetType.ShouldBe(bigDoc.DotNetType);
                slimDoc.LastModified.ShouldNotBe(default(DateTime));
                slimDoc.LastModified.ShouldBe(bigDoc.LastModified);
            }

            using (var session = theStore.OpenSession())
            {
                session.Query<DocWithMeta>().Where(d => d.DotNetType == typeof(DocWithMeta).FullName).Count().ShouldBe(1);

                var slimDoc = session.Load<StructuralTypes.DocWithMeta>(bigDoc.Id);
                slimDoc.DotNetType.ShouldBe(bigDoc.DotNetType);
                slimDoc.LastModified.ShouldNotBe(default(DateTime));
                slimDoc.LastModified.ShouldBe(bigDoc.LastModified);
            }
        }

        [Fact]
        public void doc_metadata_is_mapped_for_bulk_inserted_structural_typed_docs()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>()
                .MapDotNetTypeTo(x => x.DotNetType)
                .MapLastModifiedTo(x => x.LastModified);
            });

            var bigDoc = new DocWithMeta();

            theStore.BulkInsert(new DocWithMeta[] { bigDoc });

            using (var session = theStore.OpenSession())
            {
                var slimDoc = session.Load<StructuralTypes.DocWithMeta>(bigDoc.Id);
                slimDoc.DotNetType.ShouldBe(bigDoc.DotNetType);
                slimDoc.LastModified.ShouldNotBe(default(DateTime));
                slimDoc.LastModified.ShouldBe(bigDoc.LastModified);
            }

            using (var session = theStore.OpenSession())
            {
                session.Query<DocWithMeta>().Where(d => d.DotNetType == typeof(DocWithMeta).FullName).Count().ShouldBe(1);

                var slimDoc = session.Load<StructuralTypes.DocWithMeta>(bigDoc.Id);
                slimDoc.DotNetType.ShouldBe(bigDoc.DotNetType);
                slimDoc.LastModified.ShouldNotBe(default(DateTime));
                slimDoc.LastModified.ShouldBe(bigDoc.LastModified);
            }
        }

        [Fact]
        public void doc_metadata_is_mapped_for_doc_hierarchies()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>()
                .AddSubClassHierarchy(typeof(RedDocWithMeta), typeof(BlueDocWithMeta), typeof(GreenDocWithMeta), typeof(EmeraldGreenDocWithMeta))
                .SoftDeleted()
                .MapIsSoftDeletedTo(x => x.Deleted)
                .MapSoftDeletedAtTo(x => x.DeletedAt)
                .MapDocumentTypeTo(x => x.DocType)
                .MapDotNetTypeTo(x => x.DotNetType);
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

                var docs = session.Query<DocWithMeta>().ToList();
                docs.Count.ShouldBe(5);
                docs.Count(d => d.DotNetType == typeof(BlueDocWithMeta).FullName).ShouldBe(1);
                docs.Count(d => d.DotNetType == typeof(GreenDocWithMeta).FullName).ShouldBe(1);
                docs.Count(d => d.DotNetType == typeof(EmeraldGreenDocWithMeta).FullName).ShouldBe(1);

                var redDocs = session.Query<RedDocWithMeta>().ToList();
                redDocs.Count.ShouldBe(1);
                redDocs.First().DocType.ShouldBe("red_doc_with_meta");
                session.Delete(redDocs.First());

                var baseName = typeof(DocWithMeta).FullName;
                var baseDoc = session.Query<DocWithMeta>().Where(d => d.DotNetType == baseName).Single();
                session.Delete(baseDoc);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Query<DocWithMeta>().Count(d => d.Deleted && d.MaybeDeleted()).ShouldBe(2);
                session.Query<RedDocWithMeta>().Count(d => d.Deleted && d.MaybeDeleted()).ShouldBe(1);

                var allDocs = session.Query<DocWithMeta>().Where(x => x.MaybeDeleted()).ToList();
                allDocs.Count.ShouldBe(5);
                allDocs.Count(d => d.Deleted).ShouldBe(2);
                var deletedDocs = session.Query<DocWithMeta>().Where(x => x.IsDeleted()).ToList();
                deletedDocs.ShouldAllBe(d => d.Deleted);
                deletedDocs.ShouldAllBe(d => d.DeletedAt != null);
            }
        }

        [Fact]
        public void doc_metadata_is_mapped_for_bulk_inserted_doc_hierarchies()
        {
            StoreOptions(c =>
            {
                c.Schema.For<DocWithMeta>()
                .AddSubClassHierarchy(typeof(RedDocWithMeta), typeof(BlueDocWithMeta), typeof(GreenDocWithMeta), typeof(EmeraldGreenDocWithMeta))
                .SoftDeleted()
                .MapIsSoftDeletedTo(x => x.Deleted)
                .MapSoftDeletedAtTo(x => x.DeletedAt)
                .MapDocumentTypeTo(x => x.DocType)
                .MapDotNetTypeTo(x => x.DotNetType);
            });

            var doc = new DocWithMeta { Description = "transparent" };
            var red = new RedDocWithMeta { Description = "red doc" };
            var green = new GreenDocWithMeta { Description = "green doc" };
            var blue = new BlueDocWithMeta { Description = "blue doc" };
            var emerald = new EmeraldGreenDocWithMeta { Description = "emerald doc" };

            theStore.BulkInsert(new[] { doc, red, green, blue, emerald });

            using (var session = theStore.OpenSession())
            {
                session.Query<DocWithMeta>().Count(d => d.DocType == "BASE").ShouldBe(1);
                session.Query<DocWithMeta>().Count(d => d.DocType == "blue_doc_with_meta").ShouldBe(1);
                session.Query<DocWithMeta>().Count(d => d.DocType == "red_doc_with_meta").ShouldBe(1);
                session.Query<DocWithMeta>().Count(d => d.DocType == "green_doc_with_meta").ShouldBe(1);
                session.Query<DocWithMeta>().Count(d => d.DocType == "emerald_green_doc_with_meta").ShouldBe(1);

                var docs = session.Query<DocWithMeta>().ToList();
                docs.Count.ShouldBe(5);
                docs.Count(d => d.DotNetType == typeof(BlueDocWithMeta).FullName).ShouldBe(1);
                docs.Count(d => d.DotNetType == typeof(GreenDocWithMeta).FullName).ShouldBe(1);
                docs.Count(d => d.DotNetType == typeof(EmeraldGreenDocWithMeta).FullName).ShouldBe(1);

                var redDocs = session.Query<RedDocWithMeta>().ToList();
                redDocs.Count.ShouldBe(1);
                redDocs.First().DocType.ShouldBe("red_doc_with_meta");
                session.Delete(redDocs.First());

                var baseName = typeof(DocWithMeta).FullName;
                var baseDoc = session.Query<DocWithMeta>().Where(d => d.DotNetType == baseName).Single();
                session.Delete(baseDoc);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Query<DocWithMeta>().Count(d => d.Deleted && d.MaybeDeleted()).ShouldBe(2);
                session.Query<RedDocWithMeta>().Count(d => d.Deleted && d.MaybeDeleted()).ShouldBe(1);

                var allDocs = session.Query<DocWithMeta>().Where(x => x.MaybeDeleted()).ToList();
                allDocs.Count.ShouldBe(5);
                allDocs.Count(d => d.Deleted).ShouldBe(2);
                var deletedDocs = session.Query<DocWithMeta>().Where(x => x.IsDeleted()).ToList();
                deletedDocs.ShouldAllBe(d => d.Deleted);
                deletedDocs.ShouldAllBe(d => d.DeletedAt != null);
            }
        }
    }

    internal class DocWithMeta
    {
        public Guid Id { get; set; }
        public string Description { get; set; }
        public Guid Version { get; set; }
        public DateTime LastModified { get; set; }
        public string TenantId { get; private set; }
        public bool Deleted { get; private set; }
        public DateTime? DeletedAt { get; private set; }
        public string DocType { get; private set; }
        public string DotNetType { get; set; }
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

    internal class IncludedDocWithMeta
    {
        public Guid Id { get; set; }
        [Version]
        public Guid Version { get; private set; }
        public DateTime LastModified { get; private set; }
        [DotNetTypeMetadata]
        public string DotNetType { get; private set; }
    }

    internal class DocWithAttributeMeta
    {
        public Guid Id { get; set; }
        public string TenantId { get; private set; }
        public string DocType { get; private set; }
        public bool Deleted { get; private set; }
        public DateTime? DeletedAt { get; private set; }
        [Version]
        public Guid Version { get; set; }
        [LastModifiedMetadata]
        public DateTime LastModified { get; set; }
        [DotNetTypeMetadata]
        public string DotNetType { get; set; }
    }
}

namespace Marten.Testing.Acceptance.StructuralTypes
{
    [StructuralTyped]
    internal class DocWithMeta
    {
        public Guid Id { get; set; }
        public DateTime LastModified { get; set; }
        public string DocType { get; private set; }
        public string DotNetType { get; set; }
    }
}
