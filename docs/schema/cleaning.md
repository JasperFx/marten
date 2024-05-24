# Cleaning up database

For the purpose of automated testing where you need to carefully control the state of the database, Marten supplies few helper functions.

## Tearing Down Document Storage

Marten supplies the `IDocumentCleaner` service to quickly remove persisted document state or even to completely tear down the entire document storage.

This service is exposed as the `IDocumentStore.Advanced.Clean` property. You can see the usages of the document cleaner below:

<!-- snippet: sample_clean_out_documents -->
<a id='snippet-sample_clean_out_documents'></a>
```cs
public async Task clean_out_documents(IDocumentStore store)
{
    // Completely remove all the database schema objects related
    // to the User document type
    await store.Advanced.Clean.CompletelyRemoveAsync(typeof(User));

    // Tear down and remove all Marten related database schema objects
    await store.Advanced.Clean.CompletelyRemoveAllAsync();

    // Deletes all the documents stored in a Marten database
    await store.Advanced.Clean.DeleteAllDocumentsAsync();

    // Deletes all the event data stored in a Marten database
    await store.Advanced.Clean.DeleteAllEventDataAsync();

    // Deletes all of the persisted User documents
    await store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(User));

    // For cases where you may want to keep some document types,
    // but eliminate everything else. This is here specifically to support
    // automated testing scenarios where you have some static data that can
    // be safely reused across tests
    await store.Advanced.Clean.DeleteDocumentsExceptAsync(typeof(Company), typeof(User));
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/DocumentCleanerExamples.cs#L9-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_clean_out_documents' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also tear down all Data from the `IHost` instance using the `IHost.CleanAllMartenDataAsync()` method.

<!-- snippet: sample_clean_out_documents_ihost -->
<a id='snippet-sample_clean_out_documents_ihost'></a>
```cs
public async Task clean_out_documents(IHost host)
{
    // Clean off all Marten data in the default DocumentStore for this host
    await host.CleanAllMartenDataAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/DocumentCleanerExamples.cs#L38-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_clean_out_documents_ihost' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Reset all data

Use `IDocumentStore.Advanced.ResetAllData()` to deletes all current document, event data and then (re)applies the configured initial data.

<!-- snippet: sample_reset_all_data -->
<a id='snippet-sample_reset_all_data'></a>
```cs
theStore.Advanced.InitialDataCollection.Add(new Users());

await theStore.Advanced.ResetAllData();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/SessionMechanics/reset_all_data_usage.cs#L45-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_reset_all_data' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Use `IHost.ResetAllMartenDataAsync()` to delete all current document, event data and then (re)applies the configured initial data from the `IHost` instance.

<!-- snippet: sample_reset_all_data_ihost -->
<a id='snippet-sample_reset_all_data_ihost'></a>
```cs
using var host = await Host.CreateDefaultBuilder()
    .ConfigureServices(
        services =>
        {
            services.AddMarten(
                    opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.Logger(new TestOutputMartenLogger(_output));
                    }
                )
                .InitializeWith(new reset_all_data_usage.Users());
        }
    )
    .StartAsync();

await host.ResetAllMartenDataAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/SessionMechanics/reset_all_data_usage_ihost.cs#L21-L39' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_reset_all_data_ihost' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
