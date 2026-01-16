# Initial Baseline Data

:::tip
As of Marten V5.0, this feature requires you to either use the integration with the .Net `IHost` or use the new
`IDocumentStore.Advanced.ResetAllData()` method. See [Initial Baseline Data](/documents/initial-data) for more information.
:::

Marten supports seeding your database with initial data via the `IInitialData` interface. For example:

<!-- snippet: sample_initial-data -->
<a id='snippet-sample_initial-data'></a>
```cs
public class InitialData: IInitialData
{
    private readonly object[] _initialData;

    public InitialData(params object[] initialData)
    {
        _initialData = initialData;
    }

    public async Task Populate(IDocumentStore store, CancellationToken cancellation)
    {
        await using var session = store.LightweightSession();
        // Marten UPSERT will cater for existing records
        session.Store(_initialData);
        await session.SaveChangesAsync();
    }
}

public static class InitialDatasets
{
    public static readonly Company[] Companies =
    {
        new Company { Id = Guid.Parse("2219b6f7-7883-4629-95d5-1a8a6c74b244"), Name = "Netram Ltd." },
        new Company { Id = Guid.Parse("642a3e95-5875-498e-8ca0-93639ddfebcd"), Name = "Acme Inc." }
    };

    public static readonly User[] Users =
    {
        new User { Id = Guid.Parse("331c15b4-b7bd-44d6-a804-b6879f99a65f"),FirstName = "Danger" , LastName = "Mouse" },
        new User { Id = Guid.Parse("9d8ef25a-de9a-41e5-b72b-13f24b735883"), FirstName = "Speedy" , LastName = "Gonzales" }
    };
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Bugs/Bug_962_initial_data_populate_causing_null_ref_ex.cs#L55-L89' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_initial-data' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Add your `IInitialData` implementations as part of the configuration of your document store as follows:

<!-- snippet: sample_configuring-initial-data -->
<a id='snippet-sample_configuring-initial-data'></a>
```cs
using var host = await Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMarten(opts =>
            {
                opts.DatabaseSchemaName = "Bug962";

                opts.Connection(ConnectionSource.ConnectionString);
            })
            // Add as many implementations of IInitialData as you need
            .InitializeWith(new InitialData(InitialDatasets.Companies), new InitialData(InitialDatasets.Users));
    }).StartAsync();

var store = host.Services.GetRequiredService<IDocumentStore>();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Bugs/Bug_962_initial_data_populate_causing_null_ref_ex.cs#L20-L36' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-initial-data' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`IInitialData.Populate(IDocumentStore store)` will be executed for each configured entry as part of the initialization of your document store. They will be executed in the order they were added.

## Applying Initial Data only in Testing

We think it's common that you'll use the `IInitialData` mechanism strictly for test data setup. Let's say that you have
a set of baseline data for testing that lives in your test project:

<!-- snippet: sample_MyTestingData -->
<a id='snippet-sample_mytestingdata'></a>
```cs
public class MyTestingData: IInitialData
{
    public Task Populate(IDocumentStore store, CancellationToken cancellation)
    {
        // TODO -- add baseline test data here
        return Task.CompletedTask;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/InitialDataSamples.cs#L57-L68' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_mytestingdata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now, you'd like to use your exact application Marten configuration, but only for testing, add the `MyTestingData` initial data
set to the application's Marten configuration. You can do that as of Marten v5.1 with the `IServiceCollection.InitializeMartenWith()`
methods as shown in a sample below for a testing project:

<!-- snippet: sample_using_InitializeMartenWith -->
<a id='snippet-sample_using_initializemartenwith'></a>
```cs
// Use the configured host builder for your application
// by calling the Program.CreateHostBuilder() method from
// your application

// This would be slightly different using WebApplicationFactory,
// but the IServiceCollection mechanisms would be the same
var hostBuilder = Program.CreateHostBuilder(Array.Empty<string>());

// Add initial data to the application's Marten store
// in the test project
using var host = await hostBuilder
    .ConfigureServices(services =>
    {
        services.InitializeMartenWith<MyTestingData>();

        // or

        services.InitializeMartenWith(new MyTestingData());
    }).StartAsync();

// The MyTestingData initial data set would be applied at
// this point
var store = host.Services.GetRequiredService<IDocumentStore>();

// And in between tests, maybe do this to wipe out the store, then reapply
// MyTestingData:
await store.Advanced.ResetAllData();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/InitialDataSamples.cs#L23-L53' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_initializemartenwith' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
