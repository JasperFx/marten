using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.TestSupport;

internal class ScenarioAssertion: ScenarioStep
{
    private readonly Func<IQuerySession, CancellationToken, Task> _check;

    public ScenarioAssertion(Func<IQuerySession, CancellationToken, Task> check)
    {
        _check = check;
    }

    public override Task Execute(ProjectionScenario scenario, CancellationToken ct = default)
    {
        return _check(scenario.Session, ct);
    }
}
