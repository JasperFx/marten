#nullable enable
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Metadata;

[UnconditionalSuppressMessage("Trimming", "IL2075",
    Justification = "Class-level: PublicMethods/PublicProperties access via a Type obtained from object.GetType() / GetGenericArguments. Source instance is preserved at the StoreOptions / projection-registration boundary.")]
internal class VersionedPolicy: IDocumentPolicy
{
    public void Apply(DocumentMapping mapping)
    {
        if (mapping.DocumentType.CanBeCastTo<IVersioned>())
        {
            mapping.UseOptimisticConcurrency = true;
            mapping.Metadata.Version.Enabled = true;
            mapping.Metadata.Version.Member = mapping.DocumentType.GetProperty(nameof(IVersioned.Version));
        }

        else if (mapping.DocumentType.CanBeCastTo<IRevisioned>())
        {
            mapping.UseNumericRevisions = true;
            mapping.Metadata.Revision.Enabled = true;
            mapping.Metadata.Revision.Member = mapping.DocumentType.GetProperty(nameof(IRevisioned.Version));
        }

        // #4528: ILongVersioned is the 64-bit revision variant for documents projected
        // from a MultiStreamProjection, where Version is the global event sequence number
        // and can exceed Int32.
        else if (mapping.DocumentType.CanBeCastTo<ILongVersioned>())
        {
            mapping.UseNumericRevisions = true;
            mapping.Metadata.Revision.Enabled = true;
            mapping.Metadata.Revision.Member = mapping.DocumentType.GetProperty(nameof(ILongVersioned.Version));
        }

        if (mapping.UseOptimisticConcurrency)
        {
            mapping.Metadata.Version.Enabled = true;
            mapping.Metadata.Revision.Enabled = false;
        }

        if (mapping.UseNumericRevisions)
        {
            mapping.Metadata.Version.Enabled = false;
            mapping.Metadata.Revision.Enabled = true;
        }
    }
}
