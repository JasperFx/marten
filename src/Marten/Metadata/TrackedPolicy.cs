#nullable enable
using JasperFx.Core.Reflection;
using Marten.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Metadata;

[UnconditionalSuppressMessage("Trimming", "IL2075",
    Justification = "Class-level: PublicMethods/PublicProperties access via a Type obtained from object.GetType() / GetGenericArguments. Source instance is preserved at the StoreOptions / projection-registration boundary.")]
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
