using System;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core.Reflection;
using NpgsqlTypes;

namespace Marten.Schema.Arguments;

internal class ExpectedVersionArgument: UpsertArgument
{
    public ExpectedVersionArgument(NpgsqlDbType dbType)
    {
        Arg = "expected_version";
        Column = SchemaConstants.ExpectedVersionColumn;
        DbType = dbType;
        PostgresType = dbType == NpgsqlDbType.Bigint ? "bigint" : "uuid";
    }

    public override void GenerateBulkWriterCodeAsync(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
    {
        if (mapping.Metadata.Version.Member != null)
        {
            writeGuidExpectedVersion(load, mapping.Metadata.Version.Member);
        }
        else if (mapping.Metadata.Revision.Member != null)
        {
            writeLongExpectedVersion(load, mapping.Metadata.Revision.Member);
        }
        else
        {
            load.Frames.Code($"writer.Write({typeof(DBNull).FullNameInCode()}.Value, {0});", NpgsqlDbType.Uuid);
        }
    }

    private void writeGuidExpectedVersion(GeneratedMethod load, MemberInfo member)
    {
        var memberName = member.Name;
        var dbTypeUsage = Constant.ForEnum(NpgsqlDbType.Uuid).Usage;
        load.Frames.Code(
            $"writer.Write(document.{memberName} == Guid.Empty ? (object){typeof(DBNull).FullNameInCode()}.Value : (object)document.{memberName}, {dbTypeUsage});");
    }

    private void writeLongExpectedVersion(GeneratedMethod load, MemberInfo member)
    {
        var memberName = member.Name;
        var dbTypeUsage = Constant.ForEnum(NpgsqlDbType.Bigint).Usage;
        load.Frames.Code(
            $"writer.Write(document.{memberName} <= 0 ? (object){typeof(DBNull).FullNameInCode()}.Value : (object)(long)document.{memberName}, {dbTypeUsage});");
    }
}
