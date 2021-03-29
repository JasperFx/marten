using System;
using System.Collections.Generic;
using Baseline;

namespace Marten.Events.TestSupport
{
    /// <summary>
    ///     Thrown when a ProjectionScenario fails
    /// </summary>
    public class ProjectionScenarioException: AggregateException
    {
        public ProjectionScenarioException(List<string> descriptions, List<Exception> exceptions): base(
            $"Event Projection Scenario Failure{Environment.NewLine}{descriptions.Join(Environment.NewLine)}",
            exceptions)
        {
        }
    }
}
