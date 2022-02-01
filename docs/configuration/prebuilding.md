# Pre-Building Generated Types

::: tip
Marten will fall back to generating dynamic types at runtime if the pre-generated type cannot be found
in the entry assembly of the application. Marten will write warning to the `Console` when this happens if the `TypeLoadMode.LoadFromPreBuiltAssembly` option is configured.
:::

Also see the blog post [Dynamic Code Generation in Marten V4](https://jeremydmiller.com/2021/08/04/dynamic-code-generation-in-marten-v4/).

Marten V4 extensively uses runtime code generation backed by [Roslyn runtime compilation](https://jeremydmiller.com/2018/06/04/compiling-code-at-runtime-with-lamar-part-1/) for dynamic code. This is both much more powerful than [source generators](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview) in what it allows us to actually do, but can have significant memory usage and “[cold start](https://en.wikipedia.org/wiki/Cold_start_(computing))” problems (seems to depend on exact configurations, so it’s not a given that you’ll have these issues). Fear not though, Marten v4 introduced a facility to “generate ahead” the code to greatly optimize the "cold start" and memory usage in production scenarios.

To enable the optimized cold start, there are a couple steps:

1. Set `StoreOptions.GeneratedCodeMode = TypeLoadMode.LoadFromPreBuiltAssembly` in your `DocumentStore` configuration
1. Use the [Marten command line extensions for your application](/configuration/cli)
1. Add the *LamarCodeGeneration.Commands* Nuget to your main entry project. This has the necessary command line functionality to generate and export the dynamic source code.
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
public class Program
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
                services.AddMarten(opts =>
                {
                    opts.AutoCreateSchemaObjects = AutoCreate.All;
                    opts.DatabaseSchemaName = "cli";
                    opts.Connection(ConnectionSource.ConnectionString);

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
                    opts.Projections.Add(new TripAggregationWithCustomName(), ProjectionLifecycle.Inline);
                    opts.Projections.Add(new DayProjection(), ProjectionLifecycle.Async);
                    opts.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async);

                    opts.Projections.Add(new SimpleAggregate(), ProjectionLifecycle.Inline);

                    // This is actually important to register "live" aggregations too for the code generation
                    opts.Projections.SelfAggregate<SelfAggregatingTrip>(ProjectionLifecycle.Live);
                }).AddAsyncDaemon(DaemonMode.Solo);
            });
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CommandLineRunner/Program.cs#L21-L71' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring_pre_build_types' title='Start of snippet'>anchor</a></sup>
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
