using System;
using System.Threading.Tasks;

namespace Marten.Events.TestSupport
{
    internal class ScenarioAction: ScenarioStep
    {
        private readonly Action<IEventOperations> _action;

        public ScenarioAction(Action<IEventOperations> action)
        {
            _action = action;
        }

        public override async Task Execute(ProjectionScenario scenario)
        {
            _action(scenario.Session.Events);

            if (scenario.NextStep is ScenarioAssertion)
            {
                await scenario.Session.SaveChangesAsync();
                await scenario.WaitForNonStaleData();
            }
        }
    }
}