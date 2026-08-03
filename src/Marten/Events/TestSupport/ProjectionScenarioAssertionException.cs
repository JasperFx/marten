using Marten.Exceptions;

namespace Marten.Events.TestSupport;

/// <summary>
///     Thrown when a single ProjectionScenario assertion fails, e.g. a document that should
///     exist does not. Lets test code and tooling distinguish scenario assertion failures
///     from infrastructure failures inside the aggregated ProjectionScenarioException
/// </summary>
public class ProjectionScenarioAssertionException: MartenException
{
    public ProjectionScenarioAssertionException(string message): base(message)
    {
    }
}
