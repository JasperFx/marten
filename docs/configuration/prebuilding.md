# Pre-Building Generated Types

:::tip
This feature took a pretty big leap forward with Marten V5 and is much easier to utilize than it was with V4.
:::

Marten >= V4 extensively uses runtime code generation backed by [Roslyn runtime compilation](https://jeremydmiller.com/2018/06/04/compiling-code-at-runtime-with-lamar-part-1/) for dynamic code.
This is both much more powerful than [source generators](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview) in what it allows us to actually do, but can have
significant memory usage and “[cold start](https://en.wikipedia.org/wiki/Cold_start_(computing))” problems (seems to depend on exact configurations, so it’s not a given that you’ll have these issues).
Fear not though, Marten v4 introduced a facility to “generate ahead” the code to greatly optimize the "cold start" and memory usage in production scenarios.

The code generation for document storage, event handling, event projections, and additional document stores can be done
with one of three modes as shown below:

<!-- snippet: sample_code_generation_modes -->
<a id='snippet-sample_code_generation_modes'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // This is the default. Marten will always generate
    // code dynamically at runtime
    opts.GeneratedCodeMode = TypeLoadMode.Dynamic;

    // Marten will only use types that are compiled into
    // the application assembly ahead of time. This is the
    // V4 "pre-built" model
    opts.GeneratedCodeMode = TypeLoadMode.Static;

    // New for V5. More explanation in the docs:)
    opts.GeneratedCodeMode = TypeLoadMode.Auto;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/CodeGenerationOptions.cs#L16-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_code_generation_modes' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The *Auto* mode is new for Marten V5 to alleviate usability issues for folks who did not find the command line options or pre-registration
of document types to be practical. Using the `Marten.Testing.Documents.User` document from the Marten testing suite
as an example, let's start a new document store with the `Auto` mode:

<!-- snippet: sample_document_store_for_user_document -->
<a id='snippet-sample_document_store_for_user_document'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    // ConnectionSource is a little helper in the Marten
    // test suite
    opts.Connection(ConnectionSource.ConnectionString);

    opts.GeneratedCodeMode = TypeLoadMode.Auto;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/CodeGenerationOptions.cs#L40-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_document_store_for_user_document' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

First note that I didn't do anything to tell Marten about the `User` document. When this code below is executed
for the **very first time**:

<!-- snippet: sample_save_a_single_user -->
<a id='snippet-sample_save_a_single_user'></a>
```cs
await using var session = store.LightweightSession();
var user = new User { UserName = "admin" };
session.Store(user);
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/CodeGenerationOptions.cs#L53-L60' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_save_a_single_user' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten encounters the `User` document type for the first time, and determines that it needs a type called `UserProvider1415907724`
(the numeric suffix is a repeatable hash of the generated type's full type name) that is a Marten-generated type that "knows" how
to do every possible storage or loading of the `User` document type. Marten will do one of two things next:

1. If the `UserProvider1415907724` type can be found in the main application assembly, Marten will create a new instance of that class and use that from here on out for all `User` operations
2. If the `UserProvider1415907724` type cannot be found, Marten will generate the necessary C# code at runtime, write that
   code to a new file called `UserProvider1415907724.cs` at `/Internal/Generated/DocumentStorage` from the file root of your
   .Net project directory so that the code can be compiled into the application assembly on the next compilation. Finally, Marten
   will compile the generated code at runtime and use that dynamic assembly to build the actual object for the `User` document
   type.

The hope is that if a development team uses this approach during its internal testing and debugging, the generated code will just be checked into source control and compiled
into the actually deployed binaries for the system in production deployments. Of course, if the Marten configuration changes,
you will need to delete the generated code.

:::tip
Just like ASP.Net MVC, Marten uses the `IHostEnvironment.ApplicationName` property to determine the main application assembly. If
that value is missing, Marten falls back to the `Assembly.GetEntryAssembly()` value.
:::

In some cases you may need to help Marten and .Net itself out to "know" what the application assembly and the correct project
root directory for the generated code to be written to. In test harnesses or serverless runtimes like AWS Lambda / Azure Functions you
can override the application assembly and project path with this new Marten helper:

<!-- snippet: sample_using_set_application_project -->
<a id='snippet-sample_using_set_application_project'></a>
```cs
using var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMarten(opts =>
        {
            opts.Connection("some connection string");
            opts.SetApplicationProject(typeof(User).Assembly);
        });
    })
    .StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/CodeGenerationOptions.cs#L66-L79' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_set_application_project' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Generating all Types Upfront

::: tip
Also see the blog post [Dynamic Code Generation in Marten V4](https://jeremydmiller.com/2021/08/04/dynamic-code-generation-in-marten-v4/).
:::

To use the Marten command line tooling to generate all the dynamic code upfront:

To enable the optimized cold start, there are a couple steps:

1. Use the [Marten command line extensions for your application](/configuration/cli)
1. Register all document types, [compiled query types](/documents/querying/compiled-queries), and [event store projections](/events/projections/) upfront in your `DocumentStore` configuration
1. In your deployment process, you'll need to generate the Marten code with `dotnet run -- codegen write` before actually compiling the build products that will be deployed to production

::: tip
In the near future, Marten will probably be extended with better auto-discovery features
for document types, compiled queries, and event projections to make this feature easier to use.
:::

As an example, here is the Marten configuration from the project we used to test the pre-generated source code model:

<!-- snippet: sample_configuring_pre_build_types -->
<a id='snippet-sample_configuring_pre_build_types'></a>
```cs
public static class Program
{
    public static Task<int> Main(string[] args)
    {
        return CreateHostBuilder(args).RunOaktonCommands(args);
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                services.AddMartenStore<IOtherStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.RegisterDocumentType<Target>();
                    opts.GeneratedCodeMode = TypeLoadMode.Auto;
                });

                services.AddMarten(opts =>
                {
                    opts.AutoCreateSchemaObjects = AutoCreate.All;
                    opts.DatabaseSchemaName = "cli";

                    opts.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString)
                        .WithTenants("tenant1", "tenant2", "tenant3");

                    // This is important, setting this option tells Marten to
                    // *try* to use pre-generated code at runtime
                    opts.GeneratedCodeMode = TypeLoadMode.Auto;

                    opts.Schema.For<Activity>().AddSubClass<Trip>();

                    // You have to register all persisted document types ahead of time
                    // RegisterDocumentType<T>() is the equivalent of saying Schema.For<T>()
                    // just to let Marten know that document type exists
                    opts.RegisterDocumentType<Target>();
                    opts.RegisterDocumentType<User>();

                    // If you use compiled queries, you will need to register the
                    // compiled query types with Marten ahead of time
                    opts.RegisterCompiledQueryType(typeof(FindUserByAllTheThings));

                    // Register all event store projections ahead of time
                    opts.Projections
                        .Add(new TripAggregationWithCustomName(), ProjectionLifecycle.Async);

                    opts.Projections
                        .Add(new DayProjection(), ProjectionLifecycle.Async);

                    opts.Projections
                        .Add(new DistanceProjection(), ProjectionLifecycle.Async);

                    opts.Projections
                        .Add(new SimpleAggregate(), ProjectionLifecycle.Inline);

                    // This is actually important to register "live" aggregations too for the code generation
                    opts.Projections.Snapshot<SelfAggregatingTrip>(ProjectionLifecycle.Live);
                }).AddAsyncDaemon(DaemonMode.Solo);
            });
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CommandLineRunner/Program.cs#L28-L93' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring_pre_build_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Okay, after all that, there should be a new command line option called `codegen` for your project. Assuming
that you have [Oakton](https://jasperfx.github.io/oakton) wired up as your command line parser, you can preview all
the code that Marten would generate for the known document types, compiled queries, and the event store support
with this command:

```bash
dotnet run -- codegen preview
```

::: tip
Because the generated code can easily get out of sync with the Marten configuration at development time, the Marten team recommends ignoring the generated code files in your source control so that stale generated code is never accidentally migrated to production.
:::

To write the generated code to your project directory, use:

```bash
dotnet run -- codegen write
```

This will build all the dynamic code and write it to the `/Internal/Generated/` folder of your project. The code will
be in just two files, `Events.cs` for the event store support and `DocumentStorage.cs` for everything related
to document storage. If you like, you can reformat that code and split the types to different files if you want to
browse that code -- but remember that it's generated code and that pretty well always means that it's pretty ugly code.

To clean out the generated code, use:

```bash
dotnet run -- codegen delete
```

To just prove out that the code generation is valid, use this command:

```bash
dotnet run -- codegen test
```
