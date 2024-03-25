using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Storage.Metadata;
using Weasel.Postgresql.Tables;

namespace Marten.Storage;

internal class RevisionColumn: MetadataColumn<int>, ISelectableColumn
{
    public RevisionColumn(): base(SchemaConstants.VersionColumn, x => x.CurrentRevision)
    {
        AllowNulls = false;
        DefaultExpression = "0";
        Enabled = false;
    }

    internal override UpsertArgument ToArgument()
    {
        return new RevisionArgument();
    }

    public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async,
        GeneratedMethod sync, int index,
        DocumentMapping mapping)
    {
        var versionPosition = index; //mapping.IsHierarchy() ? 3 : 2;

        async.Frames.CodeAsync(
            $"var version = await reader.GetFieldValueAsync<int>({versionPosition}, token);");
        sync.Frames.Code($"var version = reader.GetFieldValue<int>({versionPosition});");

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

        return storageStyle != StorageStyle.QueryOnly && mapping.UseNumericRevisions;
    }

    public override string AlterColumnTypeSql(Table table, TableColumn changeActual)
    {
        return $"ALTER TABLE {table.Identifier.QualifiedName} DROP COLUMN {Name};{AddColumnSql(table)}";
    }

    public override bool CanAlter(TableColumn actual)
    {
        return actual.Type.EqualsIgnoreCase("uuid");
    }
}
