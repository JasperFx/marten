using Baseline;
using Marten.Schema;

namespace Marten.Metadata
{
    internal class TrackedPolicy: IDocumentPolicy
    {
        public void Apply(DocumentMapping mapping)
        {
            if (mapping.DocumentType.CanBeCastTo<ITracked>())
            {
                mapping.Metadata.CausationId.Enabled = true;
                mapping.Metadata.CausationId.Member = mapping.DocumentType.GetProperty(nameof(ITracked.CausationId));

                mapping.Metadata.CorrelationId.Enabled = true;
                mapping.Metadata.CorrelationId.Member = mapping.DocumentType.GetProperty(nameof(ITracked.CorrelationId));

                mapping.Metadata.LastModifiedBy.Enabled = true;
                mapping.Metadata.LastModifiedBy.Member = mapping.DocumentType.GetProperty(nameof(ITracked.LastModifiedBy));
            }
        }
    }
}