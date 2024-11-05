using System;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Sessions;
using Marten.Schema;
using Weasel.Postgresql;

namespace Marten.Storage.Metadata;

internal class VersionColumn: MetadataColumn<Guid>, ISelectableColumn
{
    public VersionColumn(): base(SchemaConstants.VersionColumn, x => x.CurrentVersion)
    {
        AllowNulls = false;
        DefaultExpression = "(md5(random()::text || clock_timestamp()::text)::uuid)";
        ShouldUpdatePartials = true;
    }

    public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async,
        GeneratedMethod sync, int index,
        DocumentMapping mapping)
    {
        var versionPosition = index; //mapping.IsHierarchy() ? 3 : 2;


        async.Frames.CodeAsync(
            $"var version = await reader.GetFieldValueAsync<System.Guid>({versionPosition}, token);");
        sync.Frames.Code($"var version = reader.GetFieldValue<System.Guid>({versionPosition});");

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

    public override void WriteMetadataInUpdateStatement(IPostgresqlCommandBuilder builder, DocumentSessionBase session)
    {
        builder.Append(SchemaConstants.VersionColumn);
        builder.Append(" = ");
        builder.AppendParameter(Guid.NewGuid());
    }
}
