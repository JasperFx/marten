using Marten.Schema;

namespace Marten.Events.Projections;

/// <summary>
/// Just adds a suffix to the document alias of a projected aggregate
/// </summary>
internal class AsyncProjectionVersionPolicy : IDocumentPolicy
{
    public void Apply(DocumentMapping mapping)
    {
        if (mapping.StoreOptions.Projections.TryFindAggregate(mapping.DocumentType, out var projection))
        {
            if (projection.ProjectionVersion > 1)
            {
                mapping.Alias += "_" + projection.ProjectionVersion;
            }

            if (projection.Lifecycle == ProjectionLifecycle.Async)
            {
                mapping.UseOptimisticConcurrency = false;
                mapping.Metadata.Version.Enabled = false;
                mapping.UseNumericRevisions = true;
                mapping.Metadata.Revision.Enabled = true;
            }
        }
    }
}
