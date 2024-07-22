using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Schema.Arguments;

internal class CurrentVersionArgument: UpsertArgument
{
    public CurrentVersionArgument()
    {
        Arg = "current_version";
        PostgresType = "uuid";
        DbType = NpgsqlDbType.Uuid;
        Column = null;
    }

    public override void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        method.Frames.Code("setCurrentVersionParameter({0});", Use.Type<IGroupedParameterBuilder>());
    }
}
