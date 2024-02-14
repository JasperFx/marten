using JasperFx.CodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Marten.Storage.Metadata;

namespace Marten.Storage;

internal class RevisionColumn: MetadataColumn<int>, ISelectableColumn
{
    public RevisionColumn(): base(SchemaConstants.VersionColumn, x => x.CurrentRevision)
    {
        AllowNulls = false;
        DefaultExpression = "0";
    }

    public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async,
        GeneratedMethod sync, int index,
        DocumentMapping mapping)
    {
        var versionPosition = index; //mapping.IsHierarchy() ? 3 : 2;
        
        async.Frames.CodeAsync(
            $"var version = await reader.GetFieldValueAsync<int>({versionPosition}, token);");
        sync.Frames.Code($"var version = reader.GetFieldValue<int>({versionPosition});");

        if (storageStyle != StorageStyle.QueryOnly)
        {
            // Store it
            sync.Frames.Code("_versions[id] = version;");
            async.Frames.Code("_versions[id] = version;");
        }


        if (Member != null)
        {
            sync.Frames.SetMemberValue(Member, "version", mapping.DocumentType, generatedType);
            async.Frames.SetMemberValue(Member, "version", mapping.DocumentType, generatedType);
        }
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        if (Member != null)
        {
            return true;
        }

        return storageStyle != StorageStyle.QueryOnly && mapping.UseOptimisticConcurrency;
    }
}
