using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.TestSupport;

internal abstract class ScenarioStep
{
    public string Description { get; set; } = string.Empty;

    public abstract Task Execute(ProjectionScenario scenario, CancellationToken ct = default);
}
