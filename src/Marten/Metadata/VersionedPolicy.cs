#nullable enable
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Schema;
using Marten.Storage.Metadata;
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
            // #4614: swap the default (bigint) Revision column for the integer variant.
            // IRevisioned was the Marten 8 default, and the per-stream version it backs
            // (SingleStreamProjection) is comfortably in Int32 range. Restoring the
            // narrower column here means a V8→V9 upgrade no longer migrates the table
            // from integer to bigint, and new schemas match the V8 shape exactly.
            mapping.Metadata.Revision = new RevisionColumnInt32();
            mapping.UseNumericRevisions = true;
            mapping.Metadata.Revision.Enabled = true;
            mapping.Metadata.Revision.Member = mapping.DocumentType.GetProperty(nameof(IRevisioned.Version));
        }

        // #4528: ILongVersioned is the 64-bit revision variant for documents projected
        // from a MultiStreamProjection, where Version is the global event sequence number
        // and can exceed Int32. Keeps the default RevisionColumn (bigint).
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
