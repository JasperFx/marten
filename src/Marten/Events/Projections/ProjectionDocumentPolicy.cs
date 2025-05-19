using Marten.Events.Aggregation;
using Marten.Schema;

namespace Marten.Events.Projections;

/// <summary>
/// Makes several modifications to the documents of projections
/// </summary>
internal class ProjectionDocumentPolicy : IDocumentPolicy
{
    public void Apply(DocumentMapping mapping)
    {
        if (mapping.StoreOptions.Projections == null) return;

        if (mapping.StoreOptions.Projections.TryFindAggregate(mapping.DocumentType, out var projection))
        {
            if (projection.Version > 1)
            {
                mapping.Alias += "_" + projection.Version;
            }

            mapping.UseOptimisticConcurrency = false;
            mapping.Metadata.Version.Enabled = false;
            mapping.UseNumericRevisions = true;
            mapping.Metadata.Revision.Enabled = true;

            if (projection is IMartenAggregateProjection m)
            {
                m.ConfigureAggregateMapping(mapping, mapping.StoreOptions);
            }

        }
    }
}
