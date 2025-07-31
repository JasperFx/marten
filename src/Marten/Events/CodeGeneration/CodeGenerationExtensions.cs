using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using Weasel.Postgresql;

namespace Marten.Events.CodeGeneration;

public static class CodeGenerationExtensions
{
    public static void AppendSql(this FramesCollection collection, string sql)
    {
        collection.Code($"{{0}}.{nameof(CommandBuilder.Append)}(\"{sql}\");", Use.Type<ICommandBuilder>());
    }

    public static void AppendSql(this FramesCollection collection, char sql)
    {
        collection.Code($"{{0}}.{nameof(CommandBuilder.Append)}('{sql}');", Use.Type<ICommandBuilder>());
    }
}
