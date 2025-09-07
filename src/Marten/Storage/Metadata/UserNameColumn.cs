using System.Threading;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Events;
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

internal class UserNameColumn: MetadataColumn<string>, ISelectableColumn, IEventTableColumn
{
    public static readonly string ColumnName = "user_name";

    public UserNameColumn(): base(ColumnName, x => x.LastModifiedBy)
    {
        Enabled = false;
        ShouldUpdatePartials = true;
    }

    public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
    {
        method.IfDbReaderValueIsNotNull(index, () =>
        {
            method.AssignMemberFromReader<IEvent>(null, index, x => x.UserName);
        });
    }

    public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
    {
        method.IfDbReaderValueIsNotNullAsync(index, () =>
        {
            method.AssignMemberFromReaderAsync<IEvent>(null, index, x => x.UserName);
        });
    }

    public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index, AppendMode full)
    {
        method.SetParameterFromMember<IEvent>(index, x => x.UserName);
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
        return new UserNameArgument();
    }

    public override void WriteMetadataInUpdateStatement(ICommandBuilder builder, DocumentSessionBase session)
    {
        builder.Append(ColumnName);
        builder.Append(" = ");
        builder.AppendParameter(session.LastModifiedBy);
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }
}

internal class UserNameArgument: UpsertArgument
{
    public UserNameArgument()
    {
        Arg = "userName";
        Column = UserNameColumn.ColumnName;
        PostgresType = "varchar";
        DbType = NpgsqlDbType.Varchar;
    }

    public override void GenerateCodeToModifyDocument(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        if (mapping.Metadata.LastModifiedBy.Member != null)
        {
            method.Frames.Code($"var userName = {{0}}.{nameof(IMartenSession.LastModifiedBy)};",
                Use.Type<IMartenSession>());
            method.Frames.SetMemberValue(mapping.Metadata.LastModifiedBy.Member, "userName", mapping.DocumentType,
                type);
        }
    }

    public override void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        method.Frames.Code($"setStringParameter({parameters.Usage}, {{0}}.{nameof(IMartenSession.LastModifiedBy)});",
            Use.Type<IMartenSession>());
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
