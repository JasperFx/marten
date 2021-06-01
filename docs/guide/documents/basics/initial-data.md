# Initial Data

Marten supports seeding your database with initial data via the `IInitialData` interface. For example:

<!-- snippet: sample_initial-data -->
<!-- endSnippet -->

Add your `IInitialData` implementations as part of the configuration of your document store as follows:

<!-- snippet: sample_configuring-initial-data -->
<!-- endSnippet -->

`IInitialData.Populate(IDocumentStore store)` will be executed for each configured entry as part of the initialization of your document store. They will be executed in the order they were added.
