# JasperFx Commands in the Aspire Dashboard <Badge type="tip" text="9.4" />

The optional **`JasperFx.Aspire`** package adds Marten's command-line verbs as
clickable **custom commands** on each resource tile in the
[.NET Aspire dashboard](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dashboard).
With one extension call in your AppHost project, an operator running the
Aspire dashboard against a local or staging environment can run
**`check-env`**, **`describe`**, **`projections`**, or **`resources`** against
a live Marten service without dropping to a terminal — output streams back
into the dashboard's resource console.

Marten apps inherit this for free because they build on the shared JasperFx
command layer. See also the page on [Command Line Tooling](/configuration/cli)
for the local-terminal workflow and the
[JasperFx.Aspire package README](https://github.com/JasperFx/jasperfx/tree/master/src/JasperFx.Aspire).

## Quick start

Add the package to your **Aspire AppHost** project (not the Marten service
project itself):

```shell
dotnet add package JasperFx.Aspire
```

Then opt in on the Marten service resource:

```csharp
using JasperFx.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.MartenApi>("api")
    .WithJasperFxCommands();
```

That adds the **safe-by-default** command buttons — `check-env`, `describe`,
and `codegen` (preview only) — to the `api` resource tile. Click any of them
in the dashboard, the verb runs against the live service with the same
environment Aspire injects, and the output streams into the resource's log
view.

## The verbs that matter for Marten users

- **`check-env`** *(read-only)* — runs every registered
  [environment check](/configuration/environment-checks). Confirms the Marten
  service can reach its Postgres database, that all projection dependencies
  are wired, that required schemas exist, etc.
- **`describe`** *(read-only)* — dumps the resolved Marten `StoreOptions`
  (document mappings, event store config, projections, retry policies,
  tenancy strategy, …). Useful for verifying composite configuration.
- **`resources`** *(mutating)* — applies / patches Marten's schema objects
  (`mt_events`, document tables, indexes, functions, projection tables).
  Equivalent to `IDocumentStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync()`.
- **`projections`** *(mutating)* — runs or **rebuilds** async projections.
  The rebuild path reprocesses the event store — long-running and
  disruptive on a populated store.

## Opting in to mutating verbs

Mutating verbs are off by default. Adding them is a one-liner:

```csharp
builder.AddProject<Projects.MartenApi>("api")
    .WithJasperFxCommands(opts =>
    {
        // Adds resources + projections + codegen-write buttons.
        opts.IncludeMutatingCommands = true;
    });
```

When `IncludeMutatingCommands = true`, every mutating verb requires an
explicit **confirmation dialog** in the Aspire dashboard before it runs.
The default confirmation copy is generic ("Run `projections` on `api`?");
customize per-verb when the impact is non-obvious:

```csharp
builder.AddProject<Projects.MartenApi>("api")
    .WithJasperFxCommands(opts =>
    {
        opts.IncludeMutatingCommands = true;

        opts.For("projections").ConfirmationMessage =
            "Rebuild ALL projections for 'api'? This reprocesses the entire event store.";

        opts.For("resources").ConfirmationMessage =
            "Apply pending schema changes to the 'api' database?";
    });
```

## Per-verb tweaks

`opts.For("verb")` returns a `JasperFxCommandRegistration` that lets you
override the dashboard presentation per verb:

| Property              | Use                                                                                                                                                                                                                                     |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `DisplayName`         | Button label (defaults to a humanized verb name).                                                                                                                                                                                       |
| `DisplayDescription`  | Tooltip / extended description.                                                                                                                                                                                                         |
| `IconName`            | Fluent UI icon name; sensible defaults per verb.                                                                                                                                                                                        |
| `ConfirmationMessage` | Required for mutating verbs; setting this opts a non-mutating verb into confirmation too.                                                                                                                                               |
| `IsHighlighted`       | Pins the button to the front of the strip.                                                                                                                                                                                              |
| `UpdateState`         | Callback (`Func<UpdateCommandStateContext, ResourceCommandState>`) that controls the dashboard enabled/disabled state — useful for gating verbs to `Running` (or `Running` + the migration-resource-completed state for `projections`). |

## Adding a single verb

When the curated default set isn't quite what you want, register verbs
one-at-a-time with `WithJasperFxCommand` instead of the batch helper:

```csharp
builder.AddProject<Projects.MartenApi>("api")
    .WithJasperFxCommand("projections", "rebuild MyProjection", registration =>
    {
        registration.DisplayName = "Rebuild MyProjection";
        registration.ConfirmationMessage =
            "Rebuild MyProjection for 'api'? This reprocesses the event store.";
        registration.IsHighlighted = true;
    });
```

The second argument is the verb's fixed argument string — handy for
locking a button down to one specific projection rather than exposing the
full `projections` surface.

## Constraints

- **`JasperFx.Aspire` runs at the AppHost project layer**, not inside the Marten service itself. Adding it as a `<ProjectReference>` of the service project is a no-op.
- The verbs run in a **child process** of the Marten service, with the same environment Aspire injects into the resource. If `check-env`, `resources`, or `projections` fail to reach Aspire-managed dependencies, verify the dashboard shows the resource as `Running` first.
- Buttons require `ApplyJasperFxExtensions()` + `RunJasperFxCommandsAsync(args)` to already be wired in the Marten service's `Program.cs` (see [Command Line Tooling](/configuration/cli)). Without that wiring the verb spawn succeeds but the child process won't recognize the verb.
