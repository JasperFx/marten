#nullable enable
using JasperFx.Core.Reflection;
using Marten.Schema;
using Marten.Storage;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Metadata;

[UnconditionalSuppressMessage("Trimming", "IL2075",
    Justification = "Class-level: PublicMethods/PublicProperties access via a Type obtained from object.GetType() / GetGenericArguments. Source instance is preserved at the StoreOptions / projection-registration boundary.")]
internal class TenancyPolicy: IDocumentPolicy
{
    public void Apply(DocumentMapping mapping)
    {
        if (mapping.DocumentType.CanBeCastTo<ITenanted>())
        {
            mapping.TenancyStyle = TenancyStyle.Conjoined;
            mapping.Metadata.TenantId.Enabled = true;
            mapping.Metadata.TenantId.Member = mapping.DocumentType.GetProperty(nameof(ITenanted.TenantId));
        }
    }
}
