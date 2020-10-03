using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline.Reflection;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Internal.CodeGeneration;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    public class DocTypeArgument: UpsertArgument
    {
        private readonly static MethodInfo _getAlias = ReflectionHelper.GetMethod<DocumentMapping>(x => x.AliasFor(null));
        private static readonly MethodInfo _getType = typeof(object).GetMethod("GetType");

        public DocTypeArgument()
        {
            Arg = "docType";
            Column = DocumentMapping.DocumentTypeColumn;
            DbType = NpgsqlDbType.Varchar;
            PostgresType = "varchar";
        }

        public override void GenerateCodeToModifyDocument(GeneratedMethod method, GeneratedType type, int i, Argument parameters,
            DocumentMapping mapping, StoreOptions options)
        {
            method.Frames.Code($"var docType = _mapping.{nameof(DocumentMapping.AliasFor)}(document.GetType());");

            if (mapping.DocumentTypeMember != null)
            {
                method.Frames.SetMemberValue(mapping.DocumentTypeMember, "docType", mapping.DocumentType, type);
            }
        }

        public override void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i, Argument parameters,
            DocumentMapping mapping, StoreOptions options)
        {
            method.Frames.Code($"{parameters.Usage}[{i}].{nameof(NpgsqlParameter.NpgsqlDbType)} = {{0}};", DbType);
            method.Frames.Code($"{parameters.Usage}[{i}].{nameof(NpgsqlParameter.Value)} = docType;");
        }

        public override void GenerateBulkWriterCode(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
        {
            load.Frames.Code($"var docType = _mapping.{nameof(DocumentMapping.AliasFor)}(document.GetType());");

            load.Frames.Code($"writer.Write(docType, {{0}});", DbType);
            if (mapping.DocumentTypeMember != null)
            {
                load.Frames.SetMemberValue(mapping.DocumentTypeMember, "docType", mapping.DocumentType, type);
            }
        }
    }
}
