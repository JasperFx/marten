using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Marten.Schema.Arguments;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Storage.Metadata;

internal class HeadersColumn: MetadataColumn<Dictionary<string, object>>, IEventTableColumn
{
    public static readonly string ColumnName = "headers";

    public HeadersColumn(): base(ColumnName, x => x.Headers)
    {
        Type = "jsonb";
        Enabled = false;
    }

    public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
    {
        method.IfDbReaderValueIsNotNull(index, () =>
        {
            method.AssignMemberFromReader<IEvent>(null, index, x => x.Headers);
        });
    }

    public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
    {
        method.IfDbReaderValueIsNotNullAsync(index, () =>
        {
            method.AssignMemberFromReaderAsync<IEvent>(null, index, x => x.Headers);
        });
    }

    public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index, AppendMode full)
    {
        method.Frames.Code($"var parameter{index} = parameterBuilder.{nameof(IGroupedParameterBuilder.AppendParameter)}({{0}}.Serializer.ToJson({{1}}.{nameof(IEvent.Headers)}));", Use.Type<IMartenSession>(), Use.Type<IEvent>());
        method.Frames.Code($"parameter{index}.NpgsqlDbType = {{0}};", NpgsqlDbType.Jsonb);
    }

    internal override async Task ApplyAsync(IMartenSession martenSession, DocumentMetadata metadata, int index,
        DbDataReader reader, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(index, token).ConfigureAwait(false))
        {
            return;
        }

        metadata.Headers = await martenSession.Serializer
            .FromJsonAsync<Dictionary<string, object>>(reader, index, token).ConfigureAwait(false);
    }

    internal override void Apply(IMartenSession martenSession, DocumentMetadata metadata, int index,
        DbDataReader reader)
    {
        if (reader.IsDBNull(index))
        {
            return;
        }

        var json = reader.GetStream(index);
        metadata.Headers = martenSession.Serializer.FromJson<Dictionary<string, object>>(reader, index);
    }

    internal override void RegisterForLinqSearching(DocumentMapping mapping)
    {
        // Nothing
    }

    internal override UpsertArgument ToArgument()
    {
        return new HeadersArgument();
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }
}

internal class HeadersArgument: UpsertArgument
{
    public HeadersArgument()
    {
        Arg = "headerDict";
        Column = HeadersColumn.ColumnName;
        PostgresType = "jsonb";
        DbType = NpgsqlDbType.Jsonb;
    }

    public override void GenerateCodeToModifyDocument(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        if (mapping.Metadata.Headers.Member != null)
        {
            method.Frames.Code($"var headers = {{0}}.{nameof(IMartenSession.Headers)};",
                Use.Type<IMartenSession>());
            method.Frames.SetMemberValue(mapping.Metadata.Headers.Member, "headers", mapping.DocumentType, type);
        }
    }

    public override void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        method.Frames.Code($"setHeaderParameter({parameters.Usage}, {{0}});", Use.Type<IMartenSession>());
    }

    public override void GenerateBulkWriterCodeAsync(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
    {
        load.Frames.CodeAsync($"await writer.WriteAsync({typeof(DBNull).FullNameInCode()}.Value, {{0}}, {{1}});",
            DbType, Use.Type<CancellationToken>());
    }
}
