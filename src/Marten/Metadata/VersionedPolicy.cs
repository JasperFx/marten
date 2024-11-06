#nullable enable
using JasperFx;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Schema;

namespace Marten.Metadata;

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
