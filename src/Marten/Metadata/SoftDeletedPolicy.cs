using Baseline;
using Marten.Schema;

namespace Marten.Metadata
{
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
}