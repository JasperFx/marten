# Optimized Development Workflow

The original point of Marten was to have a persistence option that mostly got out of your way and
let developers just get things done without having to spend a lot of time fiddling with database
scripts or ORM configuration. To that end, the default configuration for Marten is optimized for
immediate developer productivity:

<!-- snippet: sample_simplest_possible_setup -->
<a id='snippet-sample_simplest_possible_setup'></a>
```cs
var store = DocumentStore.For("connection string");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/reading_configuration_from_jasperfxoptions.cs#L188-L192' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_simplest_possible_setup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In the configuration above, as needed, behind the scenes Marten is:

1. Checking the underlying database to check if the existing database
   schema matches the in memory configuration for each document type
   and the event sourcing, then applies any necessary database migrations
   at runtime
2. Generating and compiling dynamic code at runtime for each document type,
   compiled query type, the event sourcing, and some types of event projections

And that's (mostly) great at development time! However, the dynamic code compilation
comes with a nontrivial cold start drag that's unsuitable for serverless architectures
and some unnecessary sluggishness in automated testing sometimes. The automatic
database migrations may be undesirable in production as it requires significant
rights from the application to the underlying Postgresql database. Additionally,
the automatic database migrations does require a little bit of in memory locking
in the Marten code that has been problematic for folks using Marten from Blazor.

To allow for maximum developer productivity while using more efficient production
options, use this option in Marten bootstrapping:

<!-- snippet: sample_using_optimized_artifact_workflow -->
<a id='snippet-sample_using_optimized_artifact_workflow'></a>
```cs
using var host = await Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMarten("connection string");

        // In a "Production" environment, we're turning off the
        // automatic database migrations and dynamic code generation
        services.CritterStackDefaults(x =>
        {
            x.Production.GeneratedCodeMode = TypeLoadMode.Static;
            x.Production.ResourceAutoCreate = AutoCreate.None;
        });
    }).StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/reading_configuration_from_jasperfxoptions.cs#L77-L93' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_optimized_artifact_workflow' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

When using this option, if `IHostEnvironment.IsDevelopment()` as it would be on a local developer box, Marten is using:

* `StoreOptions.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate` to detect and apply database schema migrations as needed.
* `StoreOptions.GeneratedCodeMode = TypeLoadMode.Auto` to generate dynamic code if necessary, or use pre-built types when they exist. This optimizes the development workflow to avoid unnecessary code compilations when the Marten configuration isn't changed.

At production time, that changes to:

* `StoreOptions.AutoCreateSchemaObjects = AutoCreate.None` to short circuit any kind
  of automatic database change detection and migration at runtime. This is also a minor performance
  optimization that sidesteps potential locking issues.
* `StoreOptions.GeneratedCodeMode = TypeLoadMode.Auto` to generate dynamic code if necessary,
  or use pre-built types when they exist. This ensures that the application can
  function in production mode even if some dynamic types are not pre-built

In production mode, Marten also disables any source code generated at runtime from
being written to the file system.

To be more strict, you can also turn the code generation to `Static` meaning that
Marten will *not* attempt to generate any dynamic code. This assumes that all types are
pre-built ahead of time:

<!-- snippet: sample_using_optimized_artifact_workflow_static -->
<a id='snippet-sample_using_optimized_artifact_workflow_static'></a>
```cs
using var host = await Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMarten("connection string");

        // In a "Production" environment, we're turning off the
        // automatic database migrations and dynamic code generation
        services.CritterStackDefaults(x =>
        {
            x.Production.GeneratedCodeMode = TypeLoadMode.Static;
            x.Production.ResourceAutoCreate = AutoCreate.None;
        });
    }).StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/reading_configuration_from_jasperfxoptions.cs#L98-L113' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_optimized_artifact_workflow_static' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
