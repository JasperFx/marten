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
public class AppFixture : IAsyncLifetime
{
    public IAlbaHost Host { get; private set; }

    public async Task InitializeAsync()
    {
        Host = await Program.CreateHostBuilder(Array.Empty<string>())
            .StartAlbaAsync();
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/AppFixture.cs#L9-L25' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_appfixture' title='Start of snippet'>anchor</a></sup>
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
        // back to exactly what we described in InitialAccountData
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/IntegrationContext.cs#L8-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_context' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Other than simply connecting real test fixtures to the ASP.Net Core system under test (the IAlbaHost), this `IntegrationContext` utilizes another bit of Marten functionality to completely reset the database state back to only the data defined by the InitialAccountData so that we always have known data in the database before tests execute.

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

// TODO

There is still some discussion on how to leverage this: [Add testing helpers for async projections #2624](https://github.com/JasperFx/marten/issues/2624)

## Set-up a new database scheme for every test to avoid database cleanup

// TODO

There is yet to be a official way to do this and there are some issues with generated code: [Generated code creates conflicts whenever a new scheme is used on a new run #2731](https://github.com/JasperFx/marten/issues/2731)

## Additional Tips

1. **Parallel Execution**: xUnit runs tests in parallel. If your tests are not isolated, it could lead to unexpected behavior.
2. **Database Clean-Up**: You may want to clean up or reset the database state before running each test.

Feel free to consult the official documentation for [Alba](https://jasperfx.github.io/alba/), [Wolverine](https://wolverine.netlify.app/), and [xUnit](https://xunit.net/) for more in-depth information.
