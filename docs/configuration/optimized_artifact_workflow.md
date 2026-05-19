# Optimized Development Workflow

::: warning
The original "optimized development workflow" option introduced in Marten 4/5 was completely eliminated
in Marten 8.0 (and Wolverine 4.0) in favor of the "Critter Stack" common option shown here. 
:::

The original point of Marten was to have a persistence option that mostly got out of your way and
let developers just get things done without having to spend a lot of time fiddling with database
scripts or ORM configuration. To that end, the default configuration for Marten is optimized for
immediate developer productivity:

```cs
var store = DocumentStore.For("connection string");
```

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
The Roslyn runtime code-generation path was completely removed in Marten 9.0 (PR [#4461](https://github.com/JasperFx/marten/pull/4461)). The `StoreOptions.GeneratedCodeMode`, `StoreOptions.SourceCodeWritingEnabled`, `StoreOptions.GeneratedCodeOutputPath`, and `StoreOptions.AllowRuntimeCodeGeneration` properties have been **deleted** — remove any references to them from your bootstrapping. If you have an `Internal/Generated/` folder committed from a pre-9.0 Marten app, delete it and remove it from `.gitignore` — nothing reads or writes those files anymore. `CritterStackDefaults` still controls the `ResourceAutoCreate` half of the per-environment workflow, which is the remaining concern this page covers.
:::

To allow for maximum developer productivity while using more efficient production
options, use this option in Marten bootstrapping:

::: tip
`CritterStackDefaults` is defined in the shared JasperFx infrastructure that
Marten and Wolverine both consume. For the full reference of the per-environment
options and how `ResourceAutoCreate` is resolved from `JasperFxOptions`, see the
[JasperFx shared libraries documentation](https://shared-libs.jasperfx.net/).
:::

```cs
using var host = await Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMarten("connection string");

        // In a "Production" environment, we're turning off the
        // automatic database migrations.
        services.CritterStackDefaults(x =>
        {
            x.Production.ResourceAutoCreate = AutoCreate.None;
        });
    }).StartAsync();
```

The effective behavior per environment is driven entirely by `ResourceAutoCreate`:

* In `Development`: `StoreOptions.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate` to detect and apply database schema migrations as needed.
* In `Production`: `StoreOptions.AutoCreateSchemaObjects = AutoCreate.None` to short-circuit any kind of automatic database change detection and migration at runtime. This is also a minor performance optimization that sidesteps potential locking issues.
