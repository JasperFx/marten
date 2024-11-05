using System;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Sessions;
using Marten.Schema;
using Weasel.Postgresql;

namespace Marten.Storage.Metadata;

internal class LastModifiedColumn: MetadataColumn<DateTimeOffset>, ISelectableColumn
{
    public LastModifiedColumn(): base(SchemaConstants.LastModifiedColumn, x => x.LastModified)
    {
        DefaultExpression = "(transaction_timestamp())";
        Type = "timestamp with time zone";

        ShouldUpdatePartials = true;
    }

    public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async,
        GeneratedMethod sync,
        int index, DocumentMapping mapping)
    {
        var variableName = "lastModified";
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

    public override void WriteMetadataInUpdateStatement(IPostgresqlCommandBuilder builder, DocumentSessionBase session)
    {
        builder.Append(SchemaConstants.LastModifiedColumn);
        builder.Append(" = (now() at time zone 'utc')");
    }
}
