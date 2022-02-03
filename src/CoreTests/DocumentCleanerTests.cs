using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests
{

    public class DocumentCleanerTests : OneOffConfigurationsContext
    {
        private IDocumentCleaner theCleaner => theStore.Advanced.Clean;


        [Fact]
        public async Task clean_table()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.Store(new Target { Number = 5 });
            theSession.Store(new Target { Number = 6 });

            await theSession.SaveChangesAsync();
            theSession.Dispose();

            await theCleaner.DeleteDocumentsByTypeAsync(typeof(Target));

            using var session = theStore.QuerySession();
            session.Query<Target>().Count().ShouldBe(0);
        }

        [Fact]
        public async Task delete_all_documents()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new User());
            theSession.Store(new Company());
            theSession.Store(new Issue());

            await theSession.SaveChangesAsync();
            theSession.Dispose();

            await theCleaner.DeleteAllDocumentsAsync();

            using var session = theStore.QuerySession();
            session.Query<Target>().Count().ShouldBe(0);
            session.Query<User>().Count().ShouldBe(0);
            session.Query<Issue>().Count().ShouldBe(0);
            session.Query<Company>().Count().ShouldBe(0);
        }

        [Fact]
        public async Task completely_remove_document_type()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });

            await theSession.SaveChangesAsync();
            theSession.Dispose();

            var tableName = theStore.Storage.MappingFor(typeof(Target)).TableName;

            (await theStore.Tenancy.Default.Database.DocumentTables()).Contains(tableName)
                .ShouldBeTrue();

            await theCleaner.CompletelyRemoveAsync(typeof(Target));

            (await theStore.Tenancy.Default.Database.DocumentTables()).Contains(tableName)
                .ShouldBeFalse();
        }

        [Fact]
        public async Task completely_remove_document_removes_the_upsert_command_too()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });

            await theSession.SaveChangesAsync();

            var upsertName = theStore.Storage.MappingFor(typeof(Target)).As<DocumentMapping>().UpsertFunction;

            (await theStore.Tenancy.Default.Database.Functions()).ShouldContain(upsertName);

            await theCleaner.CompletelyRemoveAsync(typeof(Target));

            (await theStore.Tenancy.Default.Database.Functions()).ShouldNotContain(upsertName);

            Console.WriteLine("foo");
        }

        [Fact]
        public async Task completely_remove_everything()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new User());
            theSession.Store(new Company());
            theSession.Store(new Issue());

            await theSession.SaveChangesAsync();
            theSession.Dispose();

            await theCleaner.CompletelyRemoveAllAsync();
            var tables = await theStore.Tenancy.Default.Database.DocumentTables();
            tables.ShouldBeEmpty();

            var functions = await theStore.Tenancy.Default.Database.Functions();
            functions.Where(x => x.Name != "mt_immutable_timestamp" || x.Name != "mt_immutable_timestamptz")
                .ShouldBeEmpty();
        }

        [Fact]
        public async Task delete_all_event_data()
        {
            var streamId = Guid.NewGuid();
            theSession.Events.StartStream<Quest>(streamId, new QuestStarted());

            await theSession.SaveChangesAsync();

            await theCleaner.DeleteAllEventDataAsync();

            theSession.Events.QueryRawEventDataOnly<QuestStarted>().ShouldBeEmpty();
            (await theSession.Events.FetchStreamAsync(streamId)).ShouldBeEmpty();
        }


        [Fact]
        public async Task delete_all_event_data_async()
        {
            var streamId = Guid.NewGuid();
            theSession.Events.StartStream<Quest>(streamId, new QuestStarted());

            await theSession.SaveChangesAsync();

            await theCleaner.DeleteAllEventDataAsync();

            theSession.Events.QueryRawEventDataOnly<QuestStarted>().ShouldBeEmpty();
            (await theSession.Events.FetchStreamAsync(streamId)).ShouldBeEmpty();
        }

        private static void ShouldBeEmpty<T>(T[] documentTables)
        {
            var stillInDatabase = string.Join(",", documentTables);
            documentTables.Any().ShouldBeFalse(stillInDatabase);
        }

        [Fact]
        public async Task delete_except_types()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new User());
            theSession.Store(new Company());
            theSession.Store(new Issue());

            await theSession.SaveChangesAsync();
            theSession.Dispose();

            await theCleaner.DeleteDocumentsExceptAsync(typeof(Target), typeof(User));

            using var session = theStore.OpenSession();
            // Not cleaned off
            session.Query<Target>().Count().ShouldBe(2);
            session.Query<User>().Count().ShouldBe(1);

            // Should be cleaned off
            session.Query<Issue>().Count().ShouldBe(0);
            session.Query<Company>().Count().ShouldBe(0);
        }

        [Fact]
        public async Task delete_except_types_async()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new User());
            theSession.Store(new Company());
            theSession.Store(new Issue());

            await theSession.SaveChangesAsync();
            theSession.Dispose();

            await theCleaner.DeleteDocumentsExceptAsync(typeof(Target), typeof(User));

            using var session = theStore.OpenSession();
            // Not cleaned off
            session.Query<Target>().Count().ShouldBe(2);
            session.Query<User>().Count().ShouldBe(1);

            // Should be cleaned off
            session.Query<Issue>().Count().ShouldBe(0);
            session.Query<Company>().Count().ShouldBe(0);
        }

        [Fact]
        public async Task CanCleanSequences()
        {
            StoreOptions(opts =>
            {
                opts.Events.AddEventType(typeof(MembersJoined));
            });

            await theStore.Schema.ApplyAllConfiguredChangesToDatabaseAsync();

            var allSchemas = theStore.Tenancy.Default.Database.AllSchemaNames();

            int GetSequenceCount(IDocumentStore store)
            {
                using var session = store.QuerySession();
                return session.Query<int>(@"select count(*) from information_schema.sequences s
where s.sequence_name like ? and s.sequence_schema = any(?);", "mt_%", allSchemas).First();
            }

            GetSequenceCount(theStore).ShouldBeGreaterThan(0);

            await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

            GetSequenceCount(theStore).ShouldBe(0);

        }
    }
}
