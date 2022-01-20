using System.Threading;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Storage;
using Marten.Storage.Metadata;
using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    public class TenantIdArgument: UpsertArgument
    {
        public const string ArgName = "tenantid";
        private const string TenantIdFieldName = "_tenantId";

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
            if (mapping.Metadata.TenantId.Member != null)
            {
                method.Frames.SetMemberValue(mapping.Metadata.TenantId.Member, TenantIdFieldName, mapping.DocumentType, type);
            }
        }


        public override void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i, Argument parameters,
            DocumentMapping mapping, StoreOptions options)
        {

            method.Frames.Code($"{{0}}[{{1}}].Value = _tenantId;", parameters, i);
            method.Frames.Code("{0}[{1}].NpgsqlDbType = {2};", parameters, i, DbType);


        }

        public override void GenerateBulkWriterCode(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
        {
            load.Frames.Code($"writer.Write(tenant.TenantId, {{0}});", DbType);
            if (mapping.Metadata.TenantId.Member != null)
            {
                load.Frames.SetMemberValue(mapping.Metadata.TenantId.Member, "tenant.TenantId", mapping.DocumentType, type);
            }
        }

        public override void GenerateBulkWriterCodeAsync(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
        {
            load.Frames.CodeAsync($"await writer.WriteAsync(tenant.TenantId, {{0}}, {{1}});", DbType, Use.Type<CancellationToken>());
            if (mapping.Metadata.TenantId.Member != null)
            {
                load.Frames.SetMemberValue(mapping.Metadata.TenantId.Member, "tenant.TenantId", mapping.DocumentType, type);
            }
        }
    }
}
