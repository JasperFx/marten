using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Events.TestSupport;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.EventProjections;

public class projection_scenario_quality_tests: OneOffConfigurationsContext
{
    [Fact]
    public async Task failed_assertions_surface_as_typed_assertion_exceptions()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline);
        });

        var ex = await Should.ThrowAsync<ProjectionScenarioException>(async () =>
        {
            await theStore.Advanced.EventProjectionScenario(scenario =>
            {
                var id = Guid.NewGuid();
                scenario.Append(Guid.NewGuid(), new CreateUser { UserId = id, UserName = "Kareem" });

                // Fails: the user document DOES exist
                scenario.DocumentShouldNotExist<User>(id);
            }, TestContext.Current.CancellationToken);
        });

        ex.InnerExceptions.Single().ShouldBeOfType<ProjectionScenarioAssertionException>();
    }

    [Fact]
    public async Task a_failed_action_stops_the_scenario_and_skips_the_remaining_steps()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline);
        });

        var ex = await Should.ThrowAsync<ProjectionScenarioException>(async () =>
        {
            await theStore.Advanced.EventProjectionScenario(scenario =>
            {
                scenario.AppendEvents("An action that blows up", _ => throw new DivideByZeroException("boom"));

                // Neither of these should run -- the second one would fail loudly if it did
                scenario.DocumentShouldNotExist<User>(Guid.NewGuid());
                scenario.DocumentShouldExist<User>(Guid.NewGuid());
            }, TestContext.Current.CancellationToken);
        });

        ex.InnerExceptions.Single().ShouldBeOfType<DivideByZeroException>();
        ex.Message.ShouldContain("Skipped the remaining 2 step(s)");
    }

    [Fact]
    public async Task start_stream_returns_the_generated_stream_id()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline);
        });

        var userId = Guid.NewGuid();
        var streamId = Guid.Empty;

        await theStore.Advanced.EventProjectionScenario(scenario =>
        {
            streamId = scenario.StartStream(new CreateUser { UserId = userId, UserName = "Oscar" });
            scenario.DocumentShouldExist<User>(userId);
        }, TestContext.Current.CancellationToken);

        streamId.ShouldNotBe(Guid.Empty);

        var events = await theSession.Events.FetchStreamAsync(streamId, token: TestContext.Current.CancellationToken);
        events.Count.ShouldBe(1);
        events.Single().Data.ShouldBeOfType<CreateUser>().UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task a_scenario_cannot_be_executed_twice()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline);
        });

        var scenario = new ProjectionScenario(theStore);
        scenario.Append(Guid.NewGuid(), new CreateUser { UserId = Guid.NewGuid(), UserName = "Once" });

        await scenario.Execute(TestContext.Current.CancellationToken);

        // The steps were consumed by the first run, so a second run would be a silent no-op.
        // It should be a loud failure instead.
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await scenario.Execute(TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task opting_out_of_deleting_existing_data_retains_prior_events_and_documents()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline);
        });

        var existingId = Guid.NewGuid();
        theSession.Events.StartStream(Guid.NewGuid(),
            new CreateUser { UserId = existingId, UserName = "Existing" });
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        await theStore.Advanced.EventProjectionScenario(scenario =>
        {
            scenario.DeleteExistingData = false;

            // Still here because the scenario did not wipe the store
            scenario.DocumentShouldExist<User>(existingId, u => u.UserName.ShouldBe("Existing"));
        }, TestContext.Current.CancellationToken);
    }
}
