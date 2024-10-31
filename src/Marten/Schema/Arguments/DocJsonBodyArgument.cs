using System.Threading;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using Marten.Internal;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core.Operations;
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


    public override void GenerateBulkWriterCode(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
    {
        load.Frames.Code(
            "writer.Write(serializer.ToJson(document), {0});",
            NpgsqlDbType.Jsonb);
    }

    public override void GenerateBulkWriterCodeAsync(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
    {
        load.Frames.CodeAsync(
            "await writer.WriteAsync(serializer.ToJson(document), {0}, {1});",
            NpgsqlDbType.Jsonb, Use.Type<CancellationToken>());
    }

    public override void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        method.Frames.Code($"var parameter{i} = {{0}}.{nameof(IGroupedParameterBuilder<NpgsqlParameter, NpgsqlDbType>.AppendParameter)}({{1}}.Serializer.ToJson(_document));", Use.Type<IGroupedParameterBuilder<NpgsqlParameter, NpgsqlDbType>>(), Use.Type<IMartenSession>());
        method.Frames.Code($"parameter{i}.NpgsqlDbType = {{0}};", NpgsqlDbType.Jsonb);
    }
}
