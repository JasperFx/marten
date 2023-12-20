using System;
using System.Threading.Tasks;
using Marten;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Events.Projections.Flattened;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_2865_configuration_assertion_with_flat_table_projections
{
    [Fact]
    public async Task should_be_able_to_assert_on_existence_of_flat_table_functions()
    {
        var appBuilder = Host.CreateApplicationBuilder();

        appBuilder.Logging
            .SetMinimumLevel(LogLevel.Information)
            .AddFilter("Marten", LogLevel.Debug);

        appBuilder.Services.AddMarten(options =>
            {
                options.Connection(ConnectionSource.ConnectionString);
                options.DatabaseSchemaName = "flat_projections";

                options.Projections.Add<FlatImportProjection>(ProjectionLifecycle.Async);
            })
            // Add this
            .ApplyAllDatabaseChangesOnStartup()
            .UseLightweightSessions()
            .OptimizeArtifactWorkflow()
            .AddAsyncDaemon(DaemonMode.Solo);

        var app = appBuilder.Build();
        await app.StartAsync();

        var store = app.Services.GetRequiredService<IDocumentStore>();

// ########## Uncomment the next line to get the error ##########
        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(Guid.NewGuid(),
                new ImportStarted(DateTimeOffset.Now.AddMinutes(-1), "foo", "cust-1", 3),
                new ImportProgress("step-1", 3, 1),
                new ImportFinished(DateTimeOffset.Now));

            await session.SaveChangesAsync();
        }

        await app.StopAsync();
    }
}

public record ImportStarted(DateTimeOffset Started, string ActivityType, string CustomerId, int PlannedSteps);
public record ImportProgress(string StepName, int Records, int Invalids);
public record ImportFinished(DateTimeOffset Finished);
public record ImportFailed;

public class FlatImportProjection : FlatTableProjection
{
    // I'm telling Marten to use the same database schema as the events from
    // the Marten configuration in this application
    public FlatImportProjection() : base("import_history", SchemaNameSource.EventSchema)
    {
        // We need to explicitly add a primary key
        Table.AddColumn<Guid>("id").AsPrimaryKey();

        TeardownDataOnRebuild = true;

        Project<ImportStarted>(map =>
        {
            // Set values in the table from the event
            map.Map(x => x.ActivityType, "activity_type").NotNull();
            map.Map(x => x.CustomerId, "customer_id");
            map.Map(x => x.PlannedSteps, "total_steps")
                .DefaultValue(0);

            map.Map(x => x.Started);

            // Initial values
            map.SetValue("status", "started");
            map.SetValue("step_number", 0);
            map.SetValue("records", 0);
        });

        Project<ImportProgress>(map =>
        {
            // Add 1 to this column when this event is encountered
            map.Increment("step_number");

            // Update a running sum of records progressed
            // by the number of records on this event
            map.Increment(x => x.Records);

            map.SetValue("status", "working");
        });

        Project<ImportFinished>(map =>
        {
            map.Map(x => x.Finished);
            map.SetValue("status", "completed");
        });

        // Just gonna delete the record of any failures
        Delete<ImportFailed>();
    }
}
