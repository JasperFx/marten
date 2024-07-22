using System.Threading;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Sessions;
using Marten.Schema;
using Marten.Schema.Arguments;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Storage.Metadata;

internal class LastModifiedByColumn: MetadataColumn<string>, ISelectableColumn
{
    public static readonly string ColumnName = "last_modified_by";

    public LastModifiedByColumn(): base(ColumnName, x => x.LastModifiedBy)
    {
        Enabled = false;
        ShouldUpdatePartials = true;
    }

    public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async,
        GeneratedMethod sync,
        int index, DocumentMapping mapping)
    {
        setMemberFromReader(generatedType, async, sync, index, mapping);
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return mapping.Metadata.LastModifiedBy.EnabledWithMember();
    }

    internal override UpsertArgument ToArgument()
    {
        return new LastModifiedByArgument();
    }

    public override void WriteMetadataInUpdateStatement(ICommandBuilder builder, DocumentSessionBase session)
    {
        builder.Append(ColumnName);
        builder.Append(" = ");
        builder.AppendParameter(session.LastModifiedBy);
    }
}

internal class LastModifiedByArgument: UpsertArgument
{
    public LastModifiedByArgument()
    {
        Arg = "lastModifiedBy";
        Column = LastModifiedByColumn.ColumnName;
        PostgresType = "varchar";
        DbType = NpgsqlDbType.Varchar;
    }

    public override void GenerateCodeToModifyDocument(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        if (mapping.Metadata.LastModifiedBy.Member != null)
        {
            method.Frames.Code($"var lastModifiedBy = {{0}}.{nameof(IMartenSession.LastModifiedBy)};",
                Use.Type<IMartenSession>());
            method.Frames.SetMemberValue(mapping.Metadata.LastModifiedBy.Member, "lastModifiedBy", mapping.DocumentType,
                type);
        }
    }

    public override void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        method.Frames.Code(
            $"setStringParameter({parameters.Usage} {{0}}.{nameof(IMartenSession.LastModifiedBy)});",
            Use.Type<IMartenSession>());
    }

    public override void GenerateBulkWriterCode(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
    {
        load.Frames.Code("writer.Write(\"BULK_INSERT\", {0});", DbType);
    }

    public override void GenerateBulkWriterCodeAsync(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
    {
        load.Frames.CodeAsync("await writer.WriteAsync(\"BULK_INSERT\", {0}, {1});", DbType,
            Use.Type<CancellationToken>());
    }
}
