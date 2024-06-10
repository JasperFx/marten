using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Util;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Storage;

internal class IdColumn: TableColumn, ISelectableColumn
{
    private const string IdentityMapCode = "if (_identityMap.TryGetValue(id, out var existing)) return existing;";

    public IdColumn(DocumentMapping mapping): base("id",
        PostgresqlProvider.Instance.GetDatabaseType(mapping.InnerIdType(), mapping.EnumStorage))
    {
    }

    public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async,
        GeneratedMethod sync, int index,
        DocumentMapping mapping)

    {
        if (storageStyle == StorageStyle.QueryOnly)
        {
            return;
        }

        if (mapping.IdStrategy is StrongTypedIdGeneration st)
        {
            st.GenerateCodeForFetchingId(index, sync, async, mapping);
        }
        else
        {
            sync.Frames.Code($"var id = reader.GetFieldValue<{mapping.IdType.FullNameInCode()}>({index});");
            async.Frames.CodeAsync(
                $"var id = await reader.GetFieldValueAsync<{mapping.IdType.FullNameInCode()}>({index}, token);");
        }


        if (storageStyle != StorageStyle.Lightweight)
        {
            sync.Frames.Code(IdentityMapCode);
            async.Frames.Code(IdentityMapCode);
        }
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return storageStyle != StorageStyle.QueryOnly;
    }
}
