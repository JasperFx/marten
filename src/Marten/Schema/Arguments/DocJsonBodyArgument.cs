using System;
using System.Threading;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using Marten.Internal;
using Marten.Services;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Schema.Arguments;

internal class DocJsonBodyArgument: UpsertArgument
{
    public DocJsonBodyArgument()
    {
        Arg = "doc";
        PostgresType = "JSONB";
        DbType = NpgsqlDbType.Jsonb;
        Column = "data";
    }

    public override void GenerateBulkWriterCodeAsync(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
    {
        // Direct UTF-8 serialization via SerializerExtensions.SerializeToUtf8 — internally
        // a pooled buffer writer + sized byte[] snapshot. Skips the string materialization
        // that serializer.ToJson(document) would emit on the bulk-loader hot path.
        load.Frames.CodeAsync(
            $"await writer.WriteAsync({typeof(SerializerExtensions).FullName}.{nameof(SerializerExtensions.SerializeToUtf8)}(serializer, document), {{0}}, {{1}});",
            NpgsqlDbType.Jsonb, Use.Type<CancellationToken>());
    }

    public override void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        // Use Serializer.WriteToParameter for direct UTF-8 serialization into the
        // parameter; skips the intermediate string round-trip.
        method.Frames.Code($"var parameter{i} = {{0}}.{nameof(IGroupedParameterBuilder.AppendParameter)}<object>({typeof(DBNull).FullName}.Value);", Use.Type<IGroupedParameterBuilder>());
        method.Frames.Code($"{{0}}.Serializer.{nameof(ISerializer.WriteToParameter)}(parameter{i}, _document);", Use.Type<IMartenSession>());
    }
}
