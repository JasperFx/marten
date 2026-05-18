# Optimized Development Workflow

::: warning
The original "optimized development workflow" option introduced in Marten 4/5 was completely eliminated
in Marten 8.0 (and Wolverine 4.0) in favor of the "Critter Stack" common option shown here. 
:::

The original point of Marten was to have a persistence option that mostly got out of your way and
let developers just get things done without having to spend a lot of time fiddling with database
scripts or ORM configuration. To that end, the default configuration for Marten is optimized for
immediate developer productivity:

<!-- snippet: sample_simplest_possible_setup -->
<a id='snippet-sample_simplest_possible_setup'></a>
```cs
var store = DocumentStore.For("connection string");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/reading_configuration_from_jasperfxoptions.cs#L231-L235' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_simplest_possible_setup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In the configuration above, as needed, behind the scenes Marten is checking the
underlying database to see whether the existing database schema matches the
in-memory configuration for each document type and the event sourcing, then
applying any necessary database migrations at runtime.

That's great at development time! However, the automatic database migrations
may be undesirable in production as it requires significant rights from the
application to the underlying PostgreSQL database. Additionally, the automatic
database migrations require a little bit of in-memory locking in the Marten
code that has been problematic for folks using Marten from Blazor.

::: tip Marten 9.0
The Roslyn runtime code-generation path and the `TypeLoadMode` switch that
controlled it were retired in Marten 9.0. The closed-shape document storage
hierarchy, the source generator for compiled queries (with a reflection-built
fallback), and a small `System.Reflection.Emit` shim for secondary stores
replace what `JasperFx.RuntimeCompiler` used to handle. `StoreOptions.GeneratedCodeMode`,
`StoreOptions.ApplicationAssembly`, `StoreOptions.SourceCodeWritingEnabled`, and
`StoreOptions.GeneratedCodeOutputPath` are still on the surface for source-
compatibility but are no-ops. `CritterStackDefaults` still controls the
`ResourceAutoCreate` half of the per-environment workflow.
:::

To allow for maximum developer productivity while using more efficient production
options, use this option in Marten bootstrapping:

::: tip
`CritterStackDefaults` is defined in the shared JasperFx infrastructure that
Marten and Wolverine both consume. For the full reference of the per-environment
options and how `ResourceAutoCreate` is resolved from `JasperFxOptions`, see the
[JasperFx shared libraries documentation](https://shared-libs.jasperfx.net/).
:::

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

The `GeneratedCodeMode` line in that sample is a no-op in Marten 9.0 — it's
retained only so existing application bootstrapping compiles unchanged. The
effective behavior per environment is now driven entirely by `ResourceAutoCreate`:

* In `Development`: `StoreOptions.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate` to detect and apply database schema migrations as needed.
* In `Production`: `StoreOptions.AutoCreateSchemaObjects = AutoCreate.None` to short-circuit any kind of automatic database change detection and migration at runtime. This is also a minor performance optimization that sidesteps potential locking issues.
