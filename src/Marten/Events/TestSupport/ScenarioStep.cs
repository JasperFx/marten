using System.Threading.Tasks;

namespace Marten.Events.TestSupport
{
    internal abstract class ScenarioStep
    {
        public string Description { get; set; }

        public abstract Task Execute(ProjectionScenario scenario);
    }
}