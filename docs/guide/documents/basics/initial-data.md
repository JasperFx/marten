# Initial Data

Marten supports seeding your database with initial data via the `IInitialData` interface. For example:

<<< @/../src/Marten.Testing/Bugs/Bug_962_initial_data_populate_causing_null_ref_ex.cs#sample_initial-data

Add your `IInitialData` implementations as part of the configuration of your document store as follows:

<<< @/../src/Marten.Testing/Bugs/Bug_962_initial_data_populate_causing_null_ref_ex.cs#sample_configuring-initial-data

`IInitialData.Populate(IDocumentStore store)` will be executed for each configured entry as part of the initialization of your document store. They will be executed in the order they were added.
