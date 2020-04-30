using System;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Events;
using Marten.Testing.Harness;
using Marten.Testing.Storage;
using Shouldly;
using Xunit;
using Issue = Marten.Testing.Documents.Issue;

namespace Marten.Testing.Schema
{
    public class CleanFixture: StoreFixture
    {
        public CleanFixture() : base("cleaner")
        {
            Options.Storage.Add<SequenceCustomization.SequenceWithStart>();
        }
    }

    public class DocumentCleanerTests : StoreContext<CleanFixture>
    {
        private IDocumentCleaner theCleaner => theStore.Advanced.Clean;

        public DocumentCleanerTests(CleanFixture fixture) : base(fixture)
        {
            theStore.Advanced.Clean.CompletelyRemoveAll();
        }

        [Fact]
        public void clean_table()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.Store(new Target { Number = 5 });
            theSession.Store(new Target { Number = 6 });

            theSession.SaveChanges();
            theSession.Dispose();

            theCleaner.DeleteDocumentsFor(typeof(Target));

            using (var session = theStore.QuerySession())
            {
                session.Query<Target>().Count().ShouldBe(0);
            }
        }

        [Fact]
        public void delete_all_documents()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new User());
            theSession.Store(new Company());
            theSession.Store(new Issue());

            theSession.SaveChanges();
            theSession.Dispose();

            theCleaner.DeleteAllDocuments();

            using (var session = theStore.QuerySession())
            {
                session.Query<Target>().Count().ShouldBe(0);
                session.Query<User>().Count().ShouldBe(0);
                session.Query<Issue>().Count().ShouldBe(0);
                session.Query<Company>().Count().ShouldBe(0);
            }
        }

        [Fact]
        public void completely_remove_document_type()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });

            theSession.SaveChanges();
            theSession.Dispose();

            var tableName = theStore.Tenancy.Default.MappingFor(typeof(Target)).ToQueryableDocument().Table;

            theStore.Tenancy.Default.DbObjects.DocumentTables().Contains(tableName)
                .ShouldBeTrue();

            theCleaner.CompletelyRemove(typeof(Target));

            theStore.Tenancy.Default.DbObjects.DocumentTables().Contains(tableName)
                .ShouldBeFalse();
        }

        [Fact]
        public void completely_remove_document_removes_the_upsert_command_too()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });

            theSession.SaveChanges();

            var dbObjects = theStore.Tenancy.Default.DbObjects;

            var upsertName = theStore.Tenancy.Default.MappingFor(typeof(Target)).As<DocumentMapping>().UpsertFunction;

            dbObjects.Functions().ShouldContain(upsertName);

            theCleaner.CompletelyRemove(typeof(Target));

            dbObjects.Functions().Contains(upsertName)
                .ShouldBeFalse();
        }

        [Fact]
        public void completely_remove_everything()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new User());
            theSession.Store(new Company());
            theSession.Store(new Issue());

            theSession.SaveChanges();
            theSession.Dispose();

            theCleaner.CompletelyRemoveAll();

            var dbObjects = theStore.Tenancy.Default.DbObjects;

            ShouldBeEmpty(dbObjects.DocumentTables());
            ShouldBeEmpty(dbObjects.Functions().Where(x => x.Name != "mt_immutable_timestamp" || x.Name != "mt_immutable_timestamptz").ToArray());
        }

        [Fact]
        public void delete_all_event_data()
        {
            var streamId = Guid.NewGuid();
            theSession.Events.StartStream<Quest>(streamId, new QuestStarted());

            theSession.SaveChanges();

            theCleaner.DeleteAllEventData();

            theSession.Events.QueryRawEventDataOnly<QuestStarted>().ShouldBeEmpty();
            theSession.Events.FetchStream(streamId).ShouldBeEmpty();
        }

        private static void ShouldBeEmpty<T>(T[] documentTables)
        {
            var stillInDatabase = string.Join(",", documentTables);
            documentTables.Any().ShouldBeFalse(stillInDatabase);
        }

        [Fact]
        public void delete_except_types()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new User());
            theSession.Store(new Company());
            theSession.Store(new Issue());

            theSession.SaveChanges();
            theSession.Dispose();

            theCleaner.DeleteDocumentsExcept(typeof(Target), typeof(User));

            using (var session = theStore.OpenSession())
            {
                // Not cleaned off
                session.Query<Target>().Count().ShouldBe(2);
                session.Query<User>().Count().ShouldBe(1);

                // Should be cleaned off
                session.Query<Issue>().Count().ShouldBe(0);
                session.Query<Company>().Count().ShouldBe(0);
            }
        }

        [Fact]
        public void CanCleanSequences()
        {
            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            var allSchemas = theStore.Storage.AllSchemaNames();

            int GetSequenceCount(IDocumentStore store)
            {
                using (var session = store.QuerySession())
                {
                    return session.Query<int>(@"select count(*) from information_schema.sequences s
where s.sequence_name like ? and s.sequence_schema = any(?);", "mt_%", allSchemas).First();
                }
            }

            var sequenceCountBeforeClean = GetSequenceCount(theStore);

            theStore.Advanced.Clean.CompletelyRemoveAll();

            var sequenceCountAfterClean = GetSequenceCount(theStore);

            Assert.True(sequenceCountBeforeClean > 0);
            Assert.Equal(0, sequenceCountAfterClean);
        }
    }
}
