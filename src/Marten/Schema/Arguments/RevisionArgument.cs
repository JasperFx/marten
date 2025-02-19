using System.Threading;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using NpgsqlTypes;

namespace Marten.Schema.Arguments;

internal class RevisionArgument: UpsertArgument
{
    public RevisionArgument()
    {
        Arg = "revision";
        PostgresType = "integer";
        DbType = NpgsqlDbType.Integer;
        Column = SchemaConstants.VersionColumn;
    }

    public override void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        method.Frames.Code("setCurrentRevisionParameter(parameterBuilder);");
    }

    public override void GenerateBulkWriterCodeAsync(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
    {
        load.Frames.CodeAsync(
            "await writer.WriteAsync(1, {0}, {1});",
            NpgsqlDbType.Integer, Use.Type<CancellationToken>());
    }
}
