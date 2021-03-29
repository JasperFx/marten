using System;
using System.Threading.Tasks;

namespace Marten.Events.TestSupport
{
    internal class ScenarioAssertion: ScenarioStep
    {
        private readonly Func<IQuerySession, Task> _check;

        public ScenarioAssertion(Func<IQuerySession, Task> check)
        {
            _check = check;
        }

        public override Task Execute(ProjectionScenario scenario)
        {
            return _check(scenario.Session);
        }
    }
}