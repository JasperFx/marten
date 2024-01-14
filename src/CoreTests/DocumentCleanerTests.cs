using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests;

public class DocumentCleanerTests: OneOffConfigurationsContext
{
    private IDocumentCleaner theCleaner => TheStore.Advanced.Clean;


    [Fact]
    public async Task clean_table()
    {
        TheSession.Store(new Target { Number = 1 });
        TheSession.Store(new Target { Number = 2 });
        TheSession.Store(new Target { Number = 3 });
        TheSession.Store(new Target { Number = 4 });
        TheSession.Store(new Target { Number = 5 });
        TheSession.Store(new Target { Number = 6 });

        await TheSession.SaveChangesAsync();
        TheSession.Dispose();

        await theCleaner.DeleteDocumentsByTypeAsync(typeof(Target));

        await using var session = TheStore.QuerySession();
        session.Query<Target>().Count().ShouldBe(0);
    }

    [Fact]
    public async Task delete_all_documents()
    {
        TheSession.Store(new Target { Number = 1 });
        TheSession.Store(new Target { Number = 2 });
        TheSession.Store(new User());
        TheSession.Store(new Company());
        TheSession.Store(new Issue());

        await TheSession.SaveChangesAsync();
        TheSession.Dispose();

        await theCleaner.DeleteAllDocumentsAsync();

        await using var session = TheStore.QuerySession();
        session.Query<Target>().Count().ShouldBe(0);
        session.Query<User>().Count().ShouldBe(0);
        session.Query<Issue>().Count().ShouldBe(0);
        session.Query<Company>().Count().ShouldBe(0);
    }

    [Fact]
    public async Task completely_remove_document_type()
    {
        TheSession.Store(new Target { Number = 1 });
        TheSession.Store(new Target { Number = 2 });

        await TheSession.SaveChangesAsync();
        TheSession.Dispose();

        var tableName = TheStore.StorageFeatures.MappingFor(typeof(Target)).TableName;

        (await TheStore.Tenancy.Default.Database.DocumentTables()).Contains(tableName)
            .ShouldBeTrue();

        await theCleaner.CompletelyRemoveAsync(typeof(Target));

        (await TheStore.Tenancy.Default.Database.DocumentTables()).Contains(tableName)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task completely_remove_document_removes_the_upsert_command_too()
    {
        TheSession.Store(new Target { Number = 1 });
        TheSession.Store(new Target { Number = 2 });

        await TheSession.SaveChangesAsync();

        var upsertName = TheStore.StorageFeatures.MappingFor(typeof(Target)).As<DocumentMapping>().UpsertFunction;

        (await TheStore.Tenancy.Default.Database.Functions()).ShouldContain(upsertName);

        await theCleaner.CompletelyRemoveAsync(typeof(Target));

        (await TheStore.Tenancy.Default.Database.Functions()).ShouldNotContain(upsertName);

        Console.WriteLine("foo");
    }

    [Fact]
    public async Task completely_remove_everything()
    {
        TheSession.Store(new Target { Number = 1 });
        TheSession.Store(new Target { Number = 2 });
        TheSession.Store(new User());
        TheSession.Store(new Company());
        TheSession.Store(new Issue());

        await TheSession.SaveChangesAsync();
        TheSession.Dispose();

        await theCleaner.CompletelyRemoveAllAsync();
        var tables = await TheStore.Tenancy.Default.Database.DocumentTables();
        tables.ShouldBeEmpty();

        var functions = await TheStore.Tenancy.Default.Database.Functions();
        functions.Where(x => x.Name != "mt_immutable_timestamp" || x.Name != "mt_immutable_timestamptz")
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task delete_all_event_data()
    {
        var streamId = Guid.NewGuid();
        TheSession.Events.StartStream<Quest>(streamId, new QuestStarted());

        await TheSession.SaveChangesAsync();

        await theCleaner.DeleteAllEventDataAsync();

        TheSession.Events.QueryRawEventDataOnly<QuestStarted>().ShouldBeEmpty();
        (await TheSession.Events.FetchStreamAsync(streamId)).ShouldBeEmpty();
    }


    [Fact]
    public async Task delete_all_event_data_async()
    {
        var streamId = Guid.NewGuid();
        TheSession.Events.StartStream<Quest>(streamId, new QuestStarted());

        await TheSession.SaveChangesAsync();

        await theCleaner.DeleteAllEventDataAsync();

        TheSession.Events.QueryRawEventDataOnly<QuestStarted>().ShouldBeEmpty();
        (await TheSession.Events.FetchStreamAsync(streamId)).ShouldBeEmpty();
    }

    private static void ShouldBeEmpty<T>(T[] documentTables)
    {
        var stillInDatabase = string.Join(",", documentTables);
        documentTables.Any().ShouldBeFalse(stillInDatabase);
    }

    [Fact]
    public async Task delete_except_types()
    {
        TheSession.Store(new Target { Number = 1 });
        TheSession.Store(new Target { Number = 2 });
        TheSession.Store(new User());
        TheSession.Store(new Company());
        TheSession.Store(new Issue());

        await TheSession.SaveChangesAsync();
        TheSession.Dispose();

        await theCleaner.DeleteDocumentsExceptAsync(typeof(Target), typeof(User));

        await using var session = TheStore.LightweightSession();
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
        TheSession.Store(new Target { Number = 1 });
        TheSession.Store(new Target { Number = 2 });
        TheSession.Store(new User());
        TheSession.Store(new Company());
        TheSession.Store(new Issue());

        await TheSession.SaveChangesAsync();
        TheSession.Dispose();

        await theCleaner.DeleteDocumentsExceptAsync(typeof(Target), typeof(User));

        await using var session = TheStore.LightweightSession();
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

        await TheStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var allSchemas = TheStore.Tenancy.Default.Database.AllSchemaNames();

        int GetSequenceCount(IDocumentStore store)
        {
            using var session = store.QuerySession();
            return session.Query<int>(@"select count(*) from information_schema.sequences s
where s.sequence_name like ? and s.sequence_schema = any(?);", "mt_%", allSchemas).First();
        }

        GetSequenceCount(TheStore).ShouldBeGreaterThan(0);

        await TheStore.Advanced.Clean.CompletelyRemoveAllAsync();

        GetSequenceCount(TheStore).ShouldBe(0);
    }
}
