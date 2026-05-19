using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Weasel.Postgresql.Tables;

namespace Marten.Storage;

internal class DataColumn: TableColumn, ISelectableColumn
{
    public DataColumn(): base("data", "JSONB")
    {
        AllowNulls = false;
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return true;
    }
}
