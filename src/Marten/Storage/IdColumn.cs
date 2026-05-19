using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Storage;

internal class IdColumn: TableColumn, ISelectableColumn
{
    public IdColumn(DocumentMapping mapping): base("id",
        PostgresqlProvider.Instance.GetDatabaseType(mapping.InnerIdType(), mapping.EnumStorage))
    {
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return storageStyle != StorageStyle.QueryOnly;
    }
}
