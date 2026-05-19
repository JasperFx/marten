using Marten.Internal.CodeGeneration;
using Marten.Linq.SoftDeletes;
using Marten.Schema;

namespace Marten.Storage.Metadata;

internal class SoftDeletedColumn: MetadataColumn<bool>, ISelectableColumn
{
    public SoftDeletedColumn(): base(SchemaConstants.DeletedColumn, x => x.Deleted)
    {
        DefaultExpression = "FALSE";
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return Member != null;
    }

    internal override void RegisterForLinqSearching(DocumentMapping mapping)
    {
        if (!Enabled || Member == null)
        {
            return;
        }

        mapping.QueryMembers.ReplaceMember(Member, new IsSoftDeletedMember(Member));
    }
}
