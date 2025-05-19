#nullable enable
using Marten.Schema;

namespace Marten.Events.Aggregation;

public interface IMartenAggregateProjection
{
    /// <summary>
    /// Apply any necessary configuration to the document mapping to work with the projection and append
    /// mode
    /// </summary>
    /// <param name="mapping"></param>
    /// <param name="storeOptions"></param>
    void ConfigureAggregateMapping(DocumentMapping mapping, StoreOptions storeOptions);

}
