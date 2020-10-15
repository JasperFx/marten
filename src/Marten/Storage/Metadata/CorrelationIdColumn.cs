using System;
using System.Linq.Expressions;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Marten.Schema.Arguments;
using NpgsqlTypes;

namespace Marten.Storage.Metadata
{
    internal class CorrelationIdColumn : MetadataColumn<string>, ISelectableColumn
    {
        public static readonly string ColumnName = "correlation_id";

        public CorrelationIdColumn() : base(ColumnName, x => x.CorrelationId)
        {
            Enabled = false;
        }

        public void GenerateCode(StorageStyle storageStyle, GeneratedType generatedType, GeneratedMethod async, GeneratedMethod sync,
            int index, DocumentMapping mapping)
        {
            var variableName = "correlationId";
            var memberType = typeof(string);

            if (Member == null) return;

            generateIfValueIsNotNull(async, sync, index);

            generateCodeToSetValuesOnDocumentFromReader(generatedType, async, sync, index, mapping, variableName, memberType, Member);

            generateCloseScope(async, sync);
        }

        public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
        {
            return mapping.Metadata.CorrelationId.EnabledWithMember();
        }

        public override UpsertArgument ToArgument()
        {
            return new CorrelationIdArgument();
        }
    }

    public class CorrelationIdArgument: UpsertArgument
    {
        public CorrelationIdArgument()
        {
            Arg = "correlationId";
            Column = CorrelationIdColumn.ColumnName;
            PostgresType = "varchar";
            DbType = NpgsqlDbType.Varchar;
        }


        public override void GenerateCodeToModifyDocument(GeneratedMethod method, GeneratedType type, int i, Argument parameters,
            DocumentMapping mapping, StoreOptions options)
        {
            if (mapping.Metadata.CorrelationId.Member != null)
            {
                method.Frames.Code($"var correlationId = {{0}}.{nameof(IMartenSession.CorrelationId)};",
                    Use.Type<IMartenSession>());
                method.Frames.SetMemberValue(mapping.Metadata.CorrelationId.Member, "correlationId", mapping.DocumentType, type);
            }
        }

        public override void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i, Argument parameters,
            DocumentMapping mapping, StoreOptions options)
        {
            method.Frames.Code($"setStringParameter({parameters.Usage}[{i}], {{0}}.{nameof(IMartenSession.CorrelationId)});", Use.Type<IMartenSession>());
        }

        public override void GenerateBulkWriterCode(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
        {
            load.Frames.Code($"writer.Write(\"BULK_INSERT\", {{0}});", DbType);
        }
    }
}
