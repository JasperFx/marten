using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.TestSupport;

internal class ScenarioAction: ScenarioStep
{
    private readonly Action<IEventOperations> _action;

    public ScenarioAction(Action<IEventOperations> action)
    {
        _action = action;
    }

    public override async Task Execute(ProjectionScenario scenario, CancellationToken ct = default)
    {
        _action(scenario.Session.Events);

        if (scenario.NextStep is ScenarioAssertion)
        {
            await scenario.Session.SaveChangesAsync(ct).ConfigureAwait(false);
            await scenario.WaitForNonStaleData().ConfigureAwait(false);
        }
    }
}
