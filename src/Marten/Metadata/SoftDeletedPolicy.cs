#nullable enable
using JasperFx.Core.Reflection;
using Marten.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Metadata;

[UnconditionalSuppressMessage("Trimming", "IL2075",
    Justification = "Class-level: PublicMethods/PublicProperties access via a Type obtained from object.GetType() / GetGenericArguments. Source instance is preserved at the StoreOptions / projection-registration boundary.")]
internal class SoftDeletedPolicy: IDocumentPolicy
{
    public void Apply(DocumentMapping mapping)
    {
        if (mapping.DocumentType.CanBeCastTo<ISoftDeleted>())
        {
            mapping.DeleteStyle = DeleteStyle.SoftDelete;

            mapping.Metadata.IsSoftDeleted.Enabled = true;
            mapping.Metadata.IsSoftDeleted.Member = mapping.DocumentType.GetProperty(nameof(ISoftDeleted.Deleted));

            mapping.Metadata.SoftDeletedAt.Enabled = true;
            mapping.Metadata.SoftDeletedAt.Member =
                mapping.DocumentType.GetProperty(nameof(ISoftDeleted.DeletedAt));
        }
    }
}
