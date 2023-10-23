using System;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage.Metadata;

internal class CreatedTimestampColumn: MetadataColumn<DateTimeOffset>, ISelectableColumn
{
    public CreatedTimestampColumn(): base(SchemaConstants.CreatedTimestampColumn, x => x.CreatedTimestamp)
    {
        DefaultExpression = "(transaction_timestamp())";
        Type = "timestamp with time zone";
        Enabled = false;
    }

    public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async,
        GeneratedMethod sync,
        int index, DocumentMapping mapping)
    {
        var variableName = "created";
        var memberType = typeof(DateTimeOffset);

        if (Member == null)
        {
            return;
        }

        sync.Frames.Code($"var {variableName} = reader.GetFieldValue<{memberType.FullNameInCode()}>({index});");
        async.Frames.CodeAsync(
            $"var {variableName} = await reader.GetFieldValueAsync<{memberType.FullNameInCode()}>({index}, token);");

        sync.Frames.SetMemberValue(Member, variableName, mapping.DocumentType, generatedType);
        async.Frames.SetMemberValue(Member, variableName, mapping.DocumentType, generatedType);
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return Member != null;
    }
}
