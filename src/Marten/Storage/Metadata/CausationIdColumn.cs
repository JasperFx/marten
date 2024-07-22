using System.Threading;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Sessions;
using Marten.Schema;
using Marten.Schema.Arguments;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Storage.Metadata;

internal class CausationIdColumn: MetadataColumn<string>, ISelectableColumn, IEventTableColumn
{
    public static readonly string ColumnName = "causation_id";

    public CausationIdColumn(): base(ColumnName, x => x.CausationId)
    {
        Enabled = false;
        ShouldUpdatePartials = true;
    }

    public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
    {
        method.IfDbReaderValueIsNotNull(index, () =>
        {
            method.AssignMemberFromReader<IEvent>(null, index, x => x.CausationId);
        });
    }

    public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
    {
        method.IfDbReaderValueIsNotNullAsync(index, () =>
        {
            method.AssignMemberFromReaderAsync<IEvent>(null, index, x => x.CausationId);
        });
    }

    public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index, AppendMode full)
    {
        method.SetParameterFromMember<IEvent>(index, x => x.CausationId);
    }

    public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async,
        GeneratedMethod sync,
        int index, DocumentMapping mapping)
    {
        setMemberFromReader(generatedType, async, sync, index, mapping);
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return mapping.Metadata.CausationId.EnabledWithMember();
    }

    internal override UpsertArgument ToArgument()
    {
        return new CausationIdArgument();
    }

    public override void WriteMetadataInUpdateStatement(ICommandBuilder builder, DocumentSessionBase session)
    {
        builder.Append(ColumnName);
        builder.Append(" = ");
        builder.AppendParameter(session.CausationId);
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }
}

internal class CausationIdArgument: UpsertArgument
{
    public CausationIdArgument()
    {
        Arg = "causationId";
        Column = CausationIdColumn.ColumnName;
        PostgresType = "varchar";
        DbType = NpgsqlDbType.Varchar;
    }

    public override void GenerateCodeToModifyDocument(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        if (mapping.Metadata.CausationId.Member != null)
        {
            method.Frames.Code($"var causationId = {{0}}.{nameof(IMartenSession.CausationId)};",
                Use.Type<IMartenSession>());
            method.Frames.SetMemberValue(mapping.Metadata.CausationId.Member, "causationId", mapping.DocumentType,
                type);
        }
    }

    public override void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        method.Frames.Code($"setStringParameter({parameters.Usage}, {{0}}.{nameof(IMartenSession.CausationId)});",
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

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }
}
