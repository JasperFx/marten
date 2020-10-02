using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Internal.CodeGeneration;
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

        public override void GenerateCodeToModifyDocument(GeneratedMethod method, GeneratedType type, int i, Argument parameters,
            DocumentMapping mapping, StoreOptions options)
        {
            method.Frames.Code($"var tenantId = {{0}}.{nameof(ITenant.TenantId)};", Use.Type<ITenant>());

            if (mapping.TenantIdMember != null)
            {
                method.Frames.SetMemberValue(mapping.TenantIdMember, "tenantId", mapping.DocumentType, type);
            }
        }


        public override void GenerateCodeToSetOperationArgument(GeneratedMethod method, GeneratedType type, int i, Argument parameters,
            DocumentMapping mapping, StoreOptions options)
        {

            method.Frames.Code($"{{0}}[{{1}}].Value = tenantId;", parameters, i);
            method.Frames.Code("{0}[{1}].NpgsqlDbType = {2};", parameters, i, DbType);


        }

        public override void GenerateBulkWriterCode(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
        {
            load.Frames.Code($"writer.Write(tenant.TenantId, {{0}});", DbType);
            if (mapping.TenantIdMember != null)
            {
                load.Frames.SetMemberValue(mapping.TenantIdMember, "tenant.TenantId", mapping.DocumentType, type);
            }
        }
    }
}
