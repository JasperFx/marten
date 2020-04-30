using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class assigning_versions_to_documents
    {
        [Fact]
        public void no_version_member_by_default()
        {
            SpecificationExtensions.ShouldBeNull(DocumentMapping.For<User>().VersionMember);
        }

        [Fact]
        public void setting_version_member_opts_into_optimistic_concurrency()
        {
            DocumentMapping.For<AttVersionedDoc>()
                .UseOptimisticConcurrency.ShouldBeTrue();
        }

        [Fact]
        public void version_member_set_by_attribute()
        {
            DocumentMapping.For<AttVersionedDoc>()
                .VersionMember.Name.ShouldBe("Version");
        }

        [Fact]
        public void wrong_version_member()
        {
            Exception<ArgumentOutOfRangeException>.ShouldBeThrownBy(() =>
            {
                DocumentMapping.For<WrongVersionTypedDoc>();
            });
        }

        [Fact]
        public void set_the_version_member_through_the_fluent_interface()
        {
            using (var store = TestingDocumentStore.For(_ =>
            {
                _.Schema.For<DocThatCouldBeVersioned>().VersionedWith(x => x.Revision);
            }))
            {
                store.Storage.MappingFor(typeof(DocThatCouldBeVersioned))
                    .VersionMember.Name.ShouldBe(nameof(DocThatCouldBeVersioned.Revision));
            }
        }
    }

    public class end_to_end_versioned_docs: IntegrationContext
    {
        [Fact]
        public void save_initial_version_of_the_doc_and_see_the_initial_version_assigned()
        {
            var doc = new AttVersionedDoc();

            using (var session = theStore.OpenSession())
            {
                session.Store(doc);

                SpecificationExtensions.ShouldBeNull(session.VersionFor(doc));

                session.SaveChanges();

                SpecificationExtensions.ShouldNotBeNull(session.VersionFor(doc));
                doc.Version.ShouldNotBe(Guid.Empty);
                doc.Version.ShouldBe(session.VersionFor(doc).Value);
            }
        }

        [Fact]
        public async Task save_initial_version_of_the_doc_and_see_the_initial_version_assigned_async()
        {
            var doc = new AttVersionedDoc();

            using (var session = theStore.OpenSession())
            {
                session.Store(doc);

                SpecificationExtensions.ShouldBeNull(session.VersionFor(doc));

                await session.SaveChangesAsync();

                SpecificationExtensions.ShouldNotBeNull(session.VersionFor(doc));
                doc.Version.ShouldNotBe(Guid.Empty);
                doc.Version.ShouldBe(session.VersionFor(doc).Value);
            }
        }

        [Fact]
        public void overwrite_behavior()
        {
            Guid originalVerion = Guid.Empty;
            var doc = new AttVersionedDoc();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc);

                SpecificationExtensions.ShouldBeNull(session.VersionFor(doc));

                session.SaveChanges();

                originalVerion = doc.Version;
            }

            IDocumentSession session1 = null;
            IDocumentSession session2 = null;

            try
            {
                session1 = theStore.OpenSession();
                session2 = theStore.OpenSession(new SessionOptions
                {
                    ConcurrencyChecks = ConcurrencyChecks.Disabled
                });

                var doc1 = session1.Load<AttVersionedDoc>(doc.Id);
                doc1.Version.ShouldBe(originalVerion);
                session1.VersionFor(doc1).ShouldBe(originalVerion);
                session1.Store(doc1);

                var doc2 = session2.Load<AttVersionedDoc>(doc.Id);
                session2.Store(doc2);

                // save via session1
                session1.SaveChanges();
                doc1.Version.ShouldNotBe(originalVerion);

                // overwrite successfully w/ session2
                session2.SaveChanges();
                doc2.Version.ShouldNotBe(originalVerion);
                doc2.Version.ShouldNotBe(doc1.Version);
            }
            finally
            {
                session1?.Dispose();
                session2?.Dispose();
            }
        }

        [Fact]
        public void overwrite_behavior_with_props()
        {
            Guid originalVerion = Guid.Empty;
            var doc = new PropVersionedDoc();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc);

                SpecificationExtensions.ShouldBeNull(session.VersionFor(doc));

                session.SaveChanges();

                originalVerion = doc.Version;
            }

            IDocumentSession session1 = null;
            IDocumentSession session2 = null;

            try
            {
                session1 = theStore.OpenSession();
                session2 = theStore.OpenSession(new SessionOptions
                {
                    ConcurrencyChecks = ConcurrencyChecks.Disabled
                });

                var doc1 = session1.Load<PropVersionedDoc>(doc.Id);
                doc1.Version.ShouldBe(originalVerion);
                session1.VersionFor(doc1).ShouldBe(originalVerion);
                session1.Store(doc1);

                var doc2 = session2.Load<PropVersionedDoc>(doc.Id);
                session2.Store(doc2);

                // save via session1
                session1.SaveChanges();
                doc1.Version.ShouldNotBe(originalVerion);

                // overwrite successfully w/ session2
                session2.SaveChanges();
                doc2.Version.ShouldNotBe(originalVerion);
                doc2.Version.ShouldNotBe(doc1.Version);
            }
            finally
            {
                session1?.Dispose();
                session2?.Dispose();
            }
        }

        [Fact]
        public async Task overwrite_behavior_async()
        {
            Guid originalVerion = Guid.Empty;
            var doc = new AttVersionedDoc();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc);

                SpecificationExtensions.ShouldBeNull(session.VersionFor(doc));

                await session.SaveChangesAsync();

                originalVerion = doc.Version;
            }

            IDocumentSession session1 = null;
            IDocumentSession session2 = null;

            try
            {
                session1 = theStore.OpenSession();
                session2 = theStore.OpenSession(new SessionOptions
                {
                    ConcurrencyChecks = ConcurrencyChecks.Disabled
                });

                var doc1 = await session1.LoadAsync<AttVersionedDoc>(doc.Id);
                doc1.Version.ShouldBe(originalVerion);
                session1.VersionFor(doc1).ShouldBe(originalVerion);
                session1.Store(doc1);

                var doc2 = await session2.LoadAsync<AttVersionedDoc>(doc.Id);
                session2.Store(doc2);

                // save via session1
                await session1.SaveChangesAsync();
                doc1.Version.ShouldNotBe(originalVerion);

                // overwrite successfully w/ session2
                await session2.SaveChangesAsync();
                doc2.Version.ShouldNotBe(originalVerion);
                doc2.Version.ShouldNotBe(doc1.Version);
            }
            finally
            {
                session1?.Dispose();
                session2?.Dispose();
            }
        }

        [Fact]
        public void versions_are_assigned_during_bulk_inserts_as_field()
        {
            var docs = new AttVersionedDoc[100];
            for (int i = 0; i < docs.Length; i++)
            {
                docs[i] = new AttVersionedDoc();
            }

            theStore.BulkInsert(docs);

            foreach (var doc in docs)
            {
                doc.Version.ShouldNotBe(Guid.Empty);
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
        }

        public end_to_end_versioned_docs(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }

    public class DocThatCouldBeVersioned
    {
        public int Id;
        public Guid Revision;
    }

    public class AttVersionedDoc
    {
        public int Id;

        [Version]
        public Guid Version;
    }

    public class PropVersionedDoc
    {
        public int Id;

        [Version]
        public Guid Version;
    }

    public class WrongVersionTypedDoc
    {
        public int Id;

        [Version]
        public string Version;
    }
}
