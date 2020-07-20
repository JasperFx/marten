using System.Linq.Expressions;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Storage;
using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    public class TenantIdArgument: UpsertArgument
    {
        public const string ArgName = "tenantid";

        public TenantIdArgument()
        {
            Arg = ArgName;
            PostgresType = "varchar";
            DbType = NpgsqlDbType.Varchar;
            Column = TenantIdColumn.Name;
        }


        public override void GenerateCode(GeneratedMethod method, GeneratedType type, int i, Argument parameters,
            DocumentMapping mapping, StoreOptions options)
        {
            method.Frames.Code($"{{0}}[{{1}}].Value = {{2}}.{nameof(ITenant.TenantId)};", parameters, i, Use.Type<ITenant>());
            method.Frames.Code("{0}[{1}].NpgsqlDbType = {2};", parameters, i, DbType);
        }

        public override void GenerateBulkWriterCode(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
        {
            load.Frames.Code($"writer.Write(tenant.TenantId, {{0}});", DbType);
        }
    }
}
