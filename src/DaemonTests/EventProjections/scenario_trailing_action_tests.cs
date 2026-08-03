using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.EventProjections;

// #5126: a ScenarioAction only flushed when the NEXT queued step was an assertion, so anything a
// trailing action appended was still in the session when Execute's finally disposed it. The events
// were silently dropped, and an arrange-only scenario was a no-op that passed.
public class scenario_trailing_action_tests: OneOffConfigurationsContext
{
    [Fact]
    public async Task trailing_append_is_committed()
    {
        StoreOptions(opts => opts.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline));

        var flushed = Guid.NewGuid();
        var trailing = Guid.NewGuid();

        await theStore.Advanced.EventProjectionScenario(scenario =>
        {
            // An assertion follows this one, so it flushes through the pre-existing path.
            scenario.Append(Guid.NewGuid(), new CreateUser { UserId = flushed, UserName = "Flushed" });
            scenario.DocumentShouldExist<User>(flushed);

            // Nothing follows this one.
            scenario.Append(Guid.NewGuid(), new CreateUser { UserId = trailing, UserName = "Trailing" });
        });

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<User>(trailing)).ShouldNotBeNull();
    }

    [Fact]
    public async Task arrange_only_scenario_actually_writes()
    {
        StoreOptions(opts => opts.Projections.Add(new UserProjection(), ProjectionLifecycle.Inline));

        var id = Guid.NewGuid();

        // No assertions at all -- every step is an action, so nothing used to be committed.
        await theStore.Advanced.EventProjectionScenario(scenario =>
        {
            scenario.Append(Guid.NewGuid(), new CreateUser { UserId = id, UserName = "Arranged" });
        });

        await using var query = theStore.QuerySession();
        var user = await query.LoadAsync<User>(id);
        user.ShouldNotBeNull();
        user.UserName.ShouldBe("Arranged");
    }
}
