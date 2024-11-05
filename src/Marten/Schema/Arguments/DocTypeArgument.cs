using System.Reflection;
using System.Threading;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core.Reflection;
using Marten.Events.CodeGeneration;
using Marten.Internal.CodeGeneration;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Schema.Arguments;

internal class DocTypeArgument: UpsertArgument
{
    private static readonly MethodInfo _getAlias = ReflectionHelper.GetMethod<DocumentMapping>(x => x.AliasFor(null));
    private static readonly MethodInfo _getType = typeof(object).GetMethod("GetType");

    public DocTypeArgument()
    {
        Arg = "docType";
        Column = SchemaConstants.DocumentTypeColumn;
        DbType = NpgsqlDbType.Varchar;
        PostgresType = "varchar";
    }

    public override void GenerateCodeToModifyDocument(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        method.Frames.Code($"var docType = _mapping.{nameof(DocumentMapping.AliasFor)}(document.GetType());");

        if (mapping.Metadata.DocumentType.Member != null)
        {
            method.Frames.SetMemberValue(mapping.Metadata.DocumentType.Member, "docType", mapping.DocumentType, type);
        }
    }

    public override void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i,
        Argument parameters,
        DocumentMapping mapping, StoreOptions options)
    {
        method.Frames.Code($"{{0}}.{nameof(IGroupedParameterBuilder.AppendParameter)}(docType);", Use.Type<IGroupedParameterBuilder>());
    }

    public override void GenerateBulkWriterCode(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
    {
        load.Frames.Code($"var docType = _mapping.{nameof(DocumentMapping.AliasFor)}(document.GetType());");

        if (mapping.Metadata.DocumentType.Member != null)
        {
            load.Frames.SetMemberValue(mapping.Metadata.DocumentType.Member, "docType", mapping.DocumentType, type);
        }
    }

    public override void GenerateBulkWriterCodeAsync(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
    {
        load.Frames.Code($"var docType = _mapping.{nameof(DocumentMapping.AliasFor)}(document.GetType());");

        load.Frames.CodeAsync("await writer.WriteAsync(docType, {0}, {1});", DbType, Use.Type<CancellationToken>());
        if (mapping.Metadata.DocumentType.Member != null)
        {
            load.Frames.SetMemberValue(mapping.Metadata.DocumentType.Member, "docType", mapping.DocumentType, type);
        }
    }
}
