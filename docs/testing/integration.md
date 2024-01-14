# Integration testing

This documentation aims to guide you through the process of performing integration tests with Marten. We will go through setting up the host using [Alba](https://jasperfx.github.io/alba/), integrating with [Wolverine](https://wolverine.netlify.app/), and testing event projections. The examples provided will leverage [Alba](https://jasperfx.github.io/alba/) and [xUnit](https://xunit.net/) for testing, but integration testing should be perfectly possible using Microsoft's [WebapplicationFactory](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests) and other testing frameworks like NUnit.

## Setting Up The Database Using Docker

Although you could spin up a testing [PostgreSQL](https://www.postgresql.org/) any way you want, our prefered way of running integration tests is running a [PostgreSQL](https://www.postgresql.org/) in docker. All that's needed is a `docker-compose.yml`:

```yaml
version: '3'
services:
  postgresql:
    image: "postgres:latest"
    ports:
     - "5433:5432"
    environment:
      - POSTGRES_DATABASE=postgres
      - POSTGRES_USER=postgres
      - POSTGRES_PASSWORD=postgres
```

Before running all test, just run 

```bash
docker compose up -d
```

## Setting Up The Host Using Alba

[Alba](https://jasperfx.github.io/alba/) is a friendly library for testing ASP.NET Core applications. To perform tests with MartenDB, it's essential to set up the host for the database first.

Firstly, install Alba via NuGet:

```bash
dotnet add package Alba
```

Then, set up your system under test (`AppFixture`) to use MartenDB:

<!-- snippet: sample_integration_appfixture -->
<a id='snippet-sample_integration_appfixture'></a>
```cs
public class AppFixture: IAsyncLifetime
{
    private string SchemaName { get; } = "sch" + Guid.NewGuid().ToString().Replace("-", string.Empty);
    public IAlbaHost Host { get; private set; }

    public async Task InitializeAsync()
    {
        // This is bootstrapping the actual application using
        // its implied Program.Main() set up
        Host = await AlbaHost.For<Program>(b =>
        {
            b.ConfigureServices((context, services) =>
            {
                services.Configure<MartenSettings>(s =>
                {
                    s.SchemaName = SchemaName;
                });
            });
        });
    }

    public async Task DisposeAsync()
        {
            await Host.DisposeAsync();
        }
    }
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/AppFixture.cs#L10-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_appfixture' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To prevent spinning up the entire host (and database setup) for every test (in parallel) you could create a collection fixture to share between your tests:

<!-- snippet: sample_integration_collection -->
<a id='snippet-sample_integration_collection'></a>
```cs
[CollectionDefinition("integration")]
public class IntegrationCollection : ICollectionFixture<AppFixture>
{
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/IntegrationCollection.cs#L5-L10' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_collection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For integration testing, It can be beneficial to a have a slim base class like this one:
<!-- snippet: sample_integration_context -->
<a id='snippet-sample_integration_context'></a>
```cs
public abstract class IntegrationContext : IAsyncLifetime
{
    protected IntegrationContext(AppFixture fixture)
    {
        Host = fixture.Host;
        Store = Host.Services.GetRequiredService<IDocumentStore>();
    }
     
    public IAlbaHost Host { get; }
    public IDocumentStore Store { get; }
     
    public async Task InitializeAsync()
    {
        // Using Marten, wipe out all data and reset the state
        await Store.Advanced.ResetAllData();
    }
 
    // This is required because of the IAsyncLifetime 
    // interface. Note that I do *not* tear down database
    // state after the test. That's purposeful
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/IntegrationContext.cs#L8-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_context' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Other than simply connecting real test fixtures to the ASP.Net Core system under test (the IAlbaHost), this `IntegrationContext` utilizes another bit of Marten functionality to completely reset the database state and then (re) applying the configured initial data so that we always have known data in the database before tests execute.

## Integration test example

Finally, in your xUnit test file, the actual example using the `IntegrationContext` and `AppFixture` we setup before:

<!-- snippet: sample_integration_streaming_example -->
<a id='snippet-sample_integration_streaming_example'></a>
```cs
[Collection("integration")]
public class web_service_streaming_example: IntegrationContext
{
    private readonly IAlbaHost theHost;

    public web_service_streaming_example(AppFixture fixture) : base(fixture)
    {
        theHost = fixture.Host;
    }

    [Fact]
    public async Task stream_a_single_document_hit()
    {
        var issue = new Issue {Description = "It's bad"};

        await using (var session = Store.LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/issue/{issue.Id}");

            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var read = result.ReadAsJson<Issue>();

        read.Description.ShouldBe(issue.Description);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/Examples/web_service_streaming_tests.cs#L9-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_streaming_example' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Set-up a new database scheme for every test to avoid database cleanup

To generate a scheme name for every test you could add this to your `AppFixture` class to generate a scheme name:

<!-- snippet: sample_integration_scheme_name -->
<a id='snippet-sample_integration_scheme_name'></a>
```cs
private string SchemaName { get; } = "sch" + Guid.NewGuid().ToString().Replace("-", string.Empty);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/AppFixture.cs#L13-L15' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_scheme_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

SchemaName can not contain certain characters such as `-` and can not start with a number, so that's why it is not just a `Guid``.

You can configure your host to use this scheme name like this:

<!-- snippet: sample_integration_configure_scheme_name -->
<a id='snippet-sample_integration_configure_scheme_name'></a>
```cs
Host = await AlbaHost.For<Program>(b =>
{
    b.ConfigureServices((context, services) =>
    {
        services.Configure<MartenSettings>(s =>
        {
            s.SchemaName = SchemaName;
        });
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/AppFixture.cs#L22-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_configure_scheme_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`MartenSettings` is a custom config class, you can customize any way you'd like:

<!-- snippet: sample_integration_settings -->
<a id='snippet-sample_integration_settings'></a>
```cs
public class MartenSettings
{
    public const string SECTION = "Marten";
    public string SchemaName { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/IssueService/MartenSettings.cs#L3-L9' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_settings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now in your actual application you should configure the schema name:

<!-- snippet: sample_integration_use_scheme_name -->
<a id='snippet-sample_integration_use_scheme_name'></a>
```cs
services.AddMarten(sp =>
{
    var options = new StoreOptions();
    options.Connection(ConnectionSource.ConnectionString);
    var martenSettings = sp.GetRequiredService<IOptions<MartenSettings>>().Value;

    if (!string.IsNullOrEmpty(martenSettings.SchemaName))
    {
        options.Events.DatabaseSchemaName = martenSettings.SchemaName;
        options.DatabaseSchemaName = martenSettings.SchemaName;
    }

    return options;
}).UseLightweightSessions();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/IssueService/Startup.cs#L32-L47' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_use_scheme_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
Keep note that Marten can be configured to generate static code on startup that contains the scheme name, so it could be beneficial to keep that turned off in the tests.
:::

Alba hosts by default start with `ASPNETCORE_ENVIRONMENT=Development`, so the `AddMarten().OptimizeArtifactWorkflow()` option will not generate static code in that case as is explained here: [Development versus Production Usage](/configuration/optimized_artifact_workflow).

## Integrating with Wolverine

Whenever wolverine's messaging is used within your application, actions may be delayed. Luckily there is a method to await Wolverine processing like this:

```cs
// This method allows us to make HTTP calls into our system
// in memory with Alba, but do so within Wolverine's test support
// for message tracking to both record outgoing messages and to ensure
// that any cascaded work spawned by the initial command is completed
// before passing control back to the calling test
protected async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(Action<Scenario> configuration)
{
    IScenarioResult result = null;
     
    // The outer part is tying into Wolverine's test support
    // to "wait" for all detected message activity to complete
    var tracked = await Host.ExecuteAndWaitAsync(async () =>
    {
        // The inner part here is actually making an HTTP request
        // to the system under test with Alba
        result = await Host.Scenario(configuration);
    });
 
    return (tracked, result);
}
```

Just add above to your `IntegrationContext` class and you'll be able to execute wolverine endpoints like: 

```cs
var (tracked, _) = await TrackedHttpCall(x =>
{
    // Send a JSON post with the DebitAccount command through the HTTP endpoint
    // BUT, it's all running in process
    x.Post.Json(new WithdrawFromAccount(account.Id, 1300)).ToUrl("/accounts/debit");

    // This is the default behavior anyway, but still good to show it here
    x.StatusCodeShouldBeOk();
});

// And also assert that an AccountUpdated message was published as well
var updated = tracked.Sent.SingleMessage<AccountUpdated>();
updated.AccountId.ShouldBe(account.Id);
updated.Balance.ShouldBe(1300);
```

Furthermore it could be beneficial to disable all external wolverine transports to test in isolation, by adding this to the Alba host setup:

```cs
services.DisableAllExternalWolverineTransports();
```

## Testing Event Projections

### Setup

Marten provides **Marten.TestHelpers** plugin. That provides necessary setup.

Install it through the [Nuget package](https://www.nuget.org/packages/Marten.TestHelpers/).

```powershell
PM> Install-Package Marten.TestHelpers
```

If you are using XUnit create an `OneOffConfigurationsContext` using the `OneOffConfigurationsHelper` like this:

<!-- snippet: sample_integration_use_scheme_name -->
<a id='snippet-sample_integration_use_scheme_name'></a>
```cs
services.AddMarten(sp =>
{
    var options = new StoreOptions();
    options.Connection(ConnectionSource.ConnectionString);
    var martenSettings = sp.GetRequiredService<IOptions<MartenSettings>>().Value;

    if (!string.IsNullOrEmpty(martenSettings.SchemaName))
    {
        options.Events.DatabaseSchemaName = martenSettings.SchemaName;
        options.DatabaseSchemaName = martenSettings.SchemaName;
    }

    return options;
}).UseLightweightSessions();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/IssueService/Startup.cs#L32-L47' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_use_scheme_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And a `DaemonContext` using the `DaemonContextHelper` like this:

<!-- snippet: sample_daemon_test_context -->
<a id='snippet-sample_daemon_test_context'></a>
```cs
public abstract class DaemonContext: OneOffConfigurationsContext
{
    private readonly DaemonContextHelper _daemonContextHelper;
    protected ITestOutputHelper Output;
    public ILogger<IProjection> Logger => _daemonContextHelper.Logger;

    public DaemonContext(ITestOutputHelper output, DaemonContextHelper daemonContextHelper) : base(daemonContextHelper)
    {
        _daemonContextHelper = daemonContextHelper;
        TheStore.Advanced.Clean.DeleteAllEventData();

        TheStore.Options.Projections.DaemonLockId++;
        Output = output;
    }

    public DaemonContext(ITestOutputHelper output)
        : this(output, new DaemonContextHelper(ConnectionSource.ConnectionString, new TestLogger<IProjection>(output))) { }

    public Task<IProjectionDaemon> StartDaemon() => _daemonContextHelper.StartDaemon();
    public Task<IProjectionDaemon> StartDaemon(string tenantId) => _daemonContextHelper.StartDaemon(tenantId);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AsyncDaemon.Testing/TestingSupport/DaemonContext.cs#L18-L39' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_daemon_test_context' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Test persisting documents and events using the one off context

Inherit from `OneOffConfigurationsContext` and test your documents, events and projections without spinning a host like this:

<!-- snippet: sample_one_off_test -->
<a id='snippet-sample_one_off_test'></a>
```cs
public class stream_aggregation : OneOffConfigurationsContext
{
    [Fact]
    public async Task create_with_static_create_method()
    {
        var user = new User {UserName = "jamesworthy"};
        TheSession.Store(user);
        await TheSession.SaveChangesAsync();

        var stream = Guid.NewGuid();
        TheSession.Events.StartStream(stream, new UserStarted {UserId = user.Id});
        await TheSession.SaveChangesAsync();

        var query = TheStore.QuerySession();
        var user2 = await query.LoadAsync<User>(user.Id);
        user2.ShouldNotBeNull();

        var aggregate = await TheSession.Events.AggregateStreamAsync<SpecialUsages>(stream);
        aggregate.UserName.ShouldBe(user.UserName);
    }
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/stream_aggregation.cs#L13-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_one_off_test' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Test an async multi-stream projection

Inherit from `DaemonContext` and access the daemon like this:

<!-- snippet: sample_multi_stream_aggregation_end_to_end_test -->
<a id='snippet-sample_multi_stream_aggregation_end_to_end_test'></a>
```cs
public class multi_stream_aggregation_end_to_end: DaemonContext
{
    public multi_stream_aggregation_end_to_end(ITestOutputHelper output): base(output)
    {
    }

    [Fact]
    public async Task Bug_1947_better_is_new_logic()
    {
        Guid user1 = Guid.NewGuid();
        Guid user2 = Guid.NewGuid();
        Guid user3 = Guid.NewGuid();

        Guid issue1 = Guid.NewGuid();
        Guid issue2 = Guid.NewGuid();
        Guid issue3 = Guid.NewGuid();

        StoreOptions(opts =>
        {
            opts.Projections.AsyncMode = DaemonMode.Solo;
            opts.Projections.Add<UserIssueProjection>(ProjectionLifecycle.Async);
        });

        await using (var session = TheStore.LightweightSession())
        {
            session.Events.Append(user1, new UserCreated { UserId = user1 });
            session.Events.Append(user2, new UserCreated { UserId = user2 });
            session.Events.Append(user3, new UserCreated { UserId = user3 });

            await session.SaveChangesAsync();
        }

        using var daemon = await StartDaemon();
        await daemon.StartAllShards();

        await daemon.Tracker.WaitForShardState("UserIssue:All", 3, 15.Seconds());

        await using (var session = TheStore.LightweightSession())
        {
            session.Events.Append(issue1, new IssueCreated { UserId = user1, IssueId = issue1 });
            await session.SaveChangesAsync();
        }

        // We need to ensure that the events are not processed in a single slice to hit the IsNew issue on multiple
        // slices which is what causes the loss of information in the projection.
        await daemon.Tracker.WaitForShardState("UserIssue:All", 4, 15.Seconds());

        await using (var session = TheStore.LightweightSession())
        {
            session.Events.Append(issue2, new IssueCreated { UserId = user1, IssueId = issue2 });
            await session.SaveChangesAsync();
        }

        await daemon.Tracker.WaitForShardState("UserIssue:All", 5, 15.Seconds());

        await using (var session = TheStore.LightweightSession())
        {
            session.Events.Append(issue3, new IssueCreated { UserId = user1, IssueId = issue3 });
            await session.SaveChangesAsync();
        }

        await daemon.Tracker.WaitForShardState("UserIssue:All", 6, 15.Seconds());

        await using (var session = TheStore.QuerySession())
        {
            var doc = await session.LoadAsync<UserIssues>(user1);
            doc.Issues.Count.ShouldBe(3);
        }
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AsyncDaemon.Testing/multi_stream_aggregation_end_to_end.cs#L15-L88' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_multi_stream_aggregation_end_to_end_test' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Test an async daemon listener

Inherit from `DaemonContext` and test your listeners like this:

<!-- snippet: sample_AsyncDaemonListener_test -->
<a id='snippet-sample_asyncdaemonlistener_test'></a>
```cs
[Fact]
public async Task can_listen_for_commits_in_daemon()
{

    var listener = new FakeListener();
    StoreOptions(x =>
    {
        x.Projections.Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Async);
        x.Projections.AsyncListeners.Add(listener);
    });

    using var daemon = await StartDaemon();
    await daemon.StartAllShards();

    NumberOfStreams = 10;
    await PublishSingleThreaded();

    await daemon.Tracker.WaitForShardState("TripCustomName:All", NumberOfEvents);

    await daemon.StopAll();

    listener.Changes.Any().ShouldBeTrue();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AsyncDaemon.Testing/basic_async_daemon_tests.cs#L68-L95' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_asyncdaemonlistener_test' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Run async projections as a part of your API tests

Building async projections from your API tests is not supported yet. There is still some discussion on how to leverage this: [Add testing helpers for async projections #2624](https://github.com/JasperFx/marten/issues/2624).

## Additional Tips

1. **Parallel Execution**: xUnit runs tests in parallel. If your tests are not isolated, it could lead to unexpected behavior.
2. **Database Clean-Up**: You may want to clean up or reset the database state before running each test. Helpers are explained here: [Cleaning up database](/schema/cleaning).
