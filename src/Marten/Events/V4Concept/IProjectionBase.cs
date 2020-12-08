using Marten.Events.V4Concept.CodeGeneration;
using Marten.Internal;

namespace Marten.Events.V4Concept
{
    public interface IProjectionBase
    {
        string ProjectionName { get; }
    }

    // TODO -- still support the "I build myself up aggregator pattern"
}
