using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Daemon.Progress;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace CoreTests;

public class DocumentCleanerTests: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;
    private IDocumentCleaner theCleaner => theStore.Advanced.Clean;

    public DocumentCleanerTests(ITestOutputHelper output)
    {
        _output = output;
    }

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

        await using var session = theStore.QuerySession();
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

        await using var session = theStore.QuerySession();
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

        var tableName = theStore.StorageFeatures.MappingFor(typeof(Target)).TableName;

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

        var upsertName = theStore.StorageFeatures.MappingFor(typeof(Target)).As<DocumentMapping>().UpsertFunction;

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
        theSession.Logger = new TestOutputMartenLogger(_output);

        theSession.QueueOperation(new InsertProjectionProgress(theStore.Events,
            new EventRange(new ShardName("Projection1", "All", 1), 1000)));

        theSession.QueueOperation(new InsertProjectionProgress(theStore.Events,
            new EventRange(new ShardName("Projection2", "All", 1), 1000)));

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Quest>(streamId, new QuestStarted());

        await theSession.SaveChangesAsync();

        await theCleaner.DeleteAllEventDataAsync();

        theSession.Events.QueryRawEventDataOnly<QuestStarted>().ShouldBeEmpty();
        (await theSession.Events.FetchStreamAsync(streamId)).ShouldBeEmpty();

        var progress = await theStore.Advanced.AllProjectionProgress();
        progress.Any().ShouldBeFalse();
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

        await using var session = theStore.LightweightSession();
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

        await using var session = theStore.LightweightSession();
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

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var allSchemas = theStore.Tenancy.Default.Database.AllSchemaNames();

        async Task<int> GetSequenceCount(IDocumentStore store)
        {
            using var session = store.QuerySession();
            var values = await session.QueryAsync<int>(@"select count(*) from information_schema.sequences s
where s.sequence_name like ? and s.sequence_schema = any(?);", "mt_%", allSchemas);

            return values.First();
        }

        (await GetSequenceCount(theStore)).ShouldBeGreaterThan(0);

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

        (await GetSequenceCount(theStore)).ShouldBe(0);
    }
}
