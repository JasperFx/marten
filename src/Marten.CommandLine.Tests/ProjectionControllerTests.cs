using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten.CommandLine.Commands.Projection;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using NSubstitute;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.CommandLine.Tests;

public static class ProjectionTestingExtensions
{
    public static IProjectionDatabase HasOneDatabase(this IProjectionStore store)
    {
        var database = Substitute.For<IProjectionDatabase>();
        database.Identifier.Returns("*Default*");
        database.Parent.Returns(store);

        store.BuildDatabases().Returns(new[] { database });

        return database;
    }

    public static IProjectionDatabase[] HasDatabases(this IProjectionStore store, params string[] names)
    {
        var databases = names.Select(name =>
        {
            var database = Substitute.For<IProjectionDatabase>();
            database.Identifier.Returns(name);
            database.Parent.Returns(store);

            return database;
        }).ToArray();

        store.BuildDatabases().Returns(databases);

        return databases;
    }
}

public class ProjectionControllerTests: IProjectionHost
{
    private readonly IConsoleView theView = Substitute.For<IConsoleView>();
    private readonly ProjectionController theController;
    private readonly List<IProjectionStore> _stores = new List<IProjectionStore>();

    public ProjectionControllerTests()
    {
        theController = new ProjectionController(this, theView);
    }

    IReadOnlyList<IProjectionStore> IProjectionHost.AllStores()
    {
        return _stores;
    }

    void IProjectionHost.ListenForUserTriggeredExit()
    {
        ListeningForUserTriggeredExit = true;
    }

    public bool ListeningForUserTriggeredExit { get; private set; }

    protected readonly List<RebuildRecord> rebuilt = new();

    Task<RebuildStatus> IProjectionHost.TryRebuildShards(IProjectionDatabase database,
        IReadOnlyList<AsyncProjectionShard> asyncProjectionShards, TimeSpan? shardTimeout)
    {
        foreach (var shard in asyncProjectionShards)
        {
            var record = new RebuildRecord(database.Parent, database, shard);
            rebuilt.Add(record);
        }


        return Task.FromResult(TheRebuildStatus);
    }

    protected Dictionary<IProjectionDatabase, IReadOnlyList<AsyncProjectionShard>> started = new();
    protected RebuildStatus TheRebuildStatus = RebuildStatus.Complete;

    Task IProjectionHost.StartShards(IProjectionDatabase database, IReadOnlyList<AsyncProjectionShard> shards)
    {
        started[database] = shards;

        return Task.CompletedTask;
    }

    Task IProjectionHost.WaitForExit()
    {
        DidWaitForExit = true;
        return Task.CompletedTask;
    }

    public bool DidWaitForExit { get; private set; }

    protected IProjectionStore withStore(string name, params (string, ProjectionLifecycle)[] projections)
    {
        var store = Substitute.For<IProjectionStore>();
        store.Name.Returns(name);
        var shards = projections.Select(pair =>
        {
            var identifier = pair.Item1.Split(':');
            var projectionName = identifier[0];

            var source = Substitute.For<IProjectionSource>();
            source.ProjectionName.Returns(projectionName);

            source.Lifecycle.Returns(pair.Item2);
            return new AsyncProjectionShard(identifier[1], source);
        }).ToList();

        store.Shards.Returns(shards);

        _stores.Add(store);
        return store;
    }

    protected AsyncProjectionShard shardFor(string storeName, string shardName)
    {
        return _stores.FirstOrDefault(x => x.Name == storeName).Shards
            .FirstOrDefault(x => x.Name.Identity == shardName);
    }

    [Fact]
    public async Task display_no_stores_message_if_no_stores()
    {
        var exitCode = await theController.Execute(new ProjectionInput());
        exitCode.ShouldBeTrue();

        theView.Received().DisplayNoStoresMessage();
    }

    [Fact]
    public async Task run_list_with_only_one_store()
    {
        var store = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));

        await theController.Execute(new ProjectionInput { ListFlag = true });

        theView.Received().ListShards(store);
    }

    [Fact]
    public async Task advance_to_latest_smoke_test()
    {
        var store = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));

        await theController.Execute(new ProjectionInput { ListFlag = true, AdvanceFlag = true});

        theView.Received().ListShards(store);
    }

    [Fact]
    public async Task run_list_with_multiple_stores()
    {
        var store1 = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));


        var store2 = withStore("Other", new("Three:All", ProjectionLifecycle.Async),
            new("Four:All", ProjectionLifecycle.Inline));


        await theController.Execute(new ProjectionInput { ListFlag = true });

        theView.Received().ListShards(store1);
        theView.Received().ListShards(store2);
    }

    [Fact]
    public void filter_stores_all_defaults()
    {
        var store1 = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));

        var store2 = withStore("Other", new("Three:All", ProjectionLifecycle.Async),
            new("Four:All", ProjectionLifecycle.Inline));

        var stores = theController.FilterStores(new ProjectionInput());
        stores.ShouldHaveTheSameElementsAs<IProjectionStore>(store1, store2);
    }

    [Fact]
    public void filter_stores_with_store_flag()
    {
        var store1 = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));

        var store2 = withStore("Other", new("Three:All", ProjectionLifecycle.Async),
            new("Four:All", ProjectionLifecycle.Inline));

        var stores = theController.FilterStores(new ProjectionInput { StoreFlag = "other" });
        stores.ShouldHaveTheSameElementsAs<IProjectionStore>(store2);
    }

    [Fact]
    public void filter_stores_interactive()
    {
        var store1 = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));

        var store2 = withStore("Other", new("Three:All", ProjectionLifecycle.Async),
            new("Four:All", ProjectionLifecycle.Inline));

        var store3 = withStore("Else", new("Six:All", ProjectionLifecycle.Async),
            new("Five:All", ProjectionLifecycle.Inline));

        theView.SelectStores(Arg.Is<string[]>(a => a.SequenceEqual(new[] { "Else", "Marten", "Other" })))
            .Returns(new string[] { "Marten", "Else" });

        var stores = theController.FilterStores(new ProjectionInput { InteractiveFlag = true });

        stores.ShouldHaveTheSameElementsAs<IProjectionStore>(store1, store3);
    }

    [Fact]
    public void filter_shards_all_defaults()
    {
        var store1 = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));

        var shards = theController.FilterShards(new ProjectionInput(), store1);
        shards.ShouldHaveTheSameElementsAs(store1.Shards);
    }

    [Fact]
    public void filter_shards_by_projection_name()
    {
        var store1 = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:First", ProjectionLifecycle.Inline), new("Bar:Second", ProjectionLifecycle.Inline));

        var shards = theController.FilterShards(new ProjectionInput { ProjectionFlag = "Bar" }, store1);
        shards.Select(x => x.Name.Identity)
            .ShouldHaveTheSameElementsAs("Bar:First", "Bar:Second");
    }

    [Fact]
    public void filter_shards_interactively()
    {
        var store1 = withStore("Marten", new("Foo:First", ProjectionLifecycle.Async),
            new("Foo:Second", ProjectionLifecycle.Async),
            new("Bar:First", ProjectionLifecycle.Inline), new("Bar:Second", ProjectionLifecycle.Inline),
            ("Tom:All", ProjectionLifecycle.Async));

        theView.SelectProjections(Arg.Is<string[]>(a => a.SequenceEqual(new[] { "Bar", "Foo", "Tom" })))
            .Returns(new string[] { "Bar", "Tom" });

        var shards = theController.FilterShards(new ProjectionInput { InteractiveFlag = true }, store1);

        shards.Select(x => x.Name.Identity)
            .ShouldHaveTheSameElementsAs("Bar:First", "Bar:Second", "Tom:All");
    }

    [Fact]
    public async Task try_to_rebuild_with_no_matching_shards()
    {
        var store1 = withStore("Marten", new("Foo:First", ProjectionLifecycle.Async),
            new("Foo:Second", ProjectionLifecycle.Async),
            new("Bar:First", ProjectionLifecycle.Inline), new("Bar:Second", ProjectionLifecycle.Inline),
            ("Tom:All", ProjectionLifecycle.Async));

        await theController.Execute(new ProjectionInput { RebuildFlag = true, ProjectionFlag = "NonExistent" });

        theView.Received().DisplayNoMatchingProjections();
    }

    // TODO -- if using Run, only choose shards that are async


    [Fact]
    public async Task should_register_for_user_exit_on_run()
    {
        var store = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));

        await theController.Execute(new ProjectionInput());

        ListeningForUserTriggeredExit.ShouldBeTrue();
    }

    [Fact]
    public void filter_databases_default()
    {
        var store = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));

        var databases = store.HasDatabases("one", "two", "three");

        var filtered = theController.FilterDatabases(new ProjectionInput(), databases);
        filtered.ShouldBeSameAs(databases);
    }

    [Fact]
    public void filter_databases_with_database_flag()
    {
        var store = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));

        var databases = store.HasDatabases("one", "two", "three");

        var filtered = theController.FilterDatabases(new ProjectionInput { DatabaseFlag = "two" }, databases);
        filtered.Single().Identifier.ShouldBe("two");
    }

    [Fact]
    public void filter_databases_with_interactive_flag()
    {
        var store = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));

        var databases = store.HasDatabases("one", "two", "three");

        theView.SelectDatabases(Arg.Is<string[]>(a => a.SequenceEqual(new[] { "one", "three", "two" })))
            .Returns(new string[] { "three" });

        var filtered = theController.FilterDatabases(new ProjectionInput { InteractiveFlag = true }, databases);

        filtered.Single().Identifier.ShouldBe("three");
    }

    [Fact]
    public async Task rebuilds_all_databases_for_a_single_store()
    {
        var store = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));

        var shards = store.Shards;

        var databases = store.HasDatabases("One", "Two", "Three");

        await theController.Execute(new ProjectionInput { RebuildFlag = true });

        var expectedRebuilds = databases.SelectMany(db =>
        {
            return shards.Select(shard => new RebuildRecord(store, db, shard));
        }).ToArray();

        rebuilt.ShouldHaveTheSameElementsAs(expectedRebuilds);
    }

    [Fact]
    public async Task rebuilds_all_databases_for_a_single_store_with_shard_timeout()
    {
        var store = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));

        var shards = store.Shards;

        var databases = store.HasDatabases("One", "Two", "Three");

        // NOTE: ShardTimeout is set in ProjectInput as a test
        // but there is no means to assert this value being used by daemon
        await theController.Execute(new ProjectionInput { RebuildFlag = true, ShardTimeoutFlag = "10m" });

        var expectedRebuilds = databases.SelectMany(db =>
        {
            return shards.Select(shard => new RebuildRecord(store, db, shard));
        }).ToArray();

        rebuilt.ShouldHaveTheSameElementsAs(expectedRebuilds);
    }

    [Fact]
    public async Task rebuild_database_that_is_empty()
    {
        var store = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));

        var shards = store.Shards;

        var databases = store.HasDatabases("One");

        TheRebuildStatus = RebuildStatus.NoData;

        await theController.Execute(new ProjectionInput { RebuildFlag = true });

        theView.Received().DisplayEmptyEventsMessage(store);
    }


    [Fact]
    public async Task run_all_async_shards_databases_for_a_single_store()
    {
        var store = withStore("Marten", new("Foo:All", ProjectionLifecycle.Async),
            new("Bar:All", ProjectionLifecycle.Inline));

        var shards = store.Shards;

        var databases = store.HasDatabases("One", "Two", "Three");

        await theController.Execute(new ProjectionInput());

        started[databases[0]]
            .ShouldHaveTheSameElementsAs(shards.Where(x => x.Source.Lifecycle == ProjectionLifecycle.Async));
        started[databases[1]]
            .ShouldHaveTheSameElementsAs(shards.Where(x => x.Source.Lifecycle == ProjectionLifecycle.Async));
        started[databases[2]]
            .ShouldHaveTheSameElementsAs(shards.Where(x => x.Source.Lifecycle == ProjectionLifecycle.Async));
    }
}

public class RebuildRecord
{
    public IProjectionStore Store { get; }
    public IProjectionDatabase Database { get; }
    public AsyncProjectionShard Shard { get; }

    public RebuildRecord(IProjectionStore store, IProjectionDatabase database, AsyncProjectionShard shard)
    {
        Store = store;
        Database = database;
        Shard = shard;
    }

    protected bool Equals(RebuildRecord other)
    {
        return Store.Equals(other.Store) && Database.Equals(other.Database) && Shard.Equals(other.Shard);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != this.GetType())
        {
            return false;
        }

        return Equals((RebuildRecord)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Store, Database, Shard);
    }

    public override string ToString()
    {
        return
            $"{nameof(Store)}: {Store.Name}, {nameof(Database)}: {Database.Identifier}, {nameof(Shard)}: {Shard.Name}";
    }
}
