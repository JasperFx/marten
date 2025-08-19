using System.Collections.Generic;
using System.Linq;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Storage;

namespace Marten.Internal.CodeGeneration;

public class BulkLoaderBuilder
{
    private readonly DocumentMapping _mapping;
    private readonly string _tempTable;

    public BulkLoaderBuilder(DocumentMapping mapping)
    {
        _mapping = mapping;
        _tempTable = _mapping.TableName.Name + "_temp";
        TypeName = _mapping.DocumentType.ToSuffixedTypeName("BulkLoader");
    }

    public string TypeName { get; }

    public GeneratedType BuildType(GeneratedAssembly assembly)
    {
        var upsertFunction = _mapping.Schema.Upsert;


        var arguments = orderArgumentsForBulkWriting(upsertFunction);

        var columns = arguments.Select(x => $"\\\"{x.Column}\\\"").Join(", ");

        var type = assembly.AddType(TypeName,
            typeof(BulkLoader<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType));

        if (_mapping.IsHierarchy())
        {
            type.AllInjectedFields.Add(new InjectedField(typeof(DocumentMapping), "mapping"));
        }

        type.MethodFor("MainLoaderSql")
            .Frames
            .ReturnNewStringConstant("MAIN_LOADER_SQL",
                $"COPY {_mapping.TableName.QualifiedName}({columns}) FROM STDIN BINARY");

        type.MethodFor("TempLoaderSql").Frames
            .ReturnNewStringConstant("TEMP_LOADER_SQL", $"COPY {_tempTable}({columns}) FROM STDIN BINARY");

        type.MethodFor(nameof(CopyNewDocumentsFromTempTable))
            .Frames.ReturnNewStringConstant("COPY_NEW_DOCUMENTS_SQL", CopyNewDocumentsFromTempTable());

        type.MethodFor(nameof(OverwriteDuplicatesFromTempTable))
            .Frames.ReturnNewStringConstant("OVERWRITE_SQL", OverwriteDuplicatesFromTempTable());

        type.MethodFor(nameof(CreateTempTableForCopying))
            .Frames.ReturnNewStringConstant("CREATE_TEMP_TABLE_FOR_COPYING_SQL",
                CreateTempTableForCopying().Replace("\"", "\\\""));

        var loadAsync = type.MethodFor("LoadRowAsync");

        foreach (var argument in arguments)
        {
            argument.GenerateBulkWriterCodeAsync(type, loadAsync, _mapping);
        }

        return type;
    }

    private static List<UpsertArgument> orderArgumentsForBulkWriting(UpsertFunction upsertFunction)
    {
        var arguments = upsertFunction.OrderedArguments().Where(x => x is not CurrentVersionArgument).ToList();
        // You need the document body to go last so that any metadata pushed into the document
        // is serialized into the JSON data
        var body = arguments.OfType<DocJsonBodyArgument>().Single();
        arguments.Remove(body);
        arguments.Add(body);
        return arguments;
    }

    public string CopyNewDocumentsFromTempTable()
    {
        var table = _mapping.Schema.Table;
        var isMultiTenanted = _mapping.TenancyStyle == TenancyStyle.Conjoined;

        var storageTable = table.Identifier.QualifiedName;
        var columns = table.Columns.Where(x => x.Name != SchemaConstants.LastModifiedColumn)
            .Select(x => $"\\\"{x.Name}\\\"").Join(", ");
        var selectColumns = table.Columns.Where(x => x.Name != SchemaConstants.LastModifiedColumn)
            .Select(x => $"{_tempTable}.\\\"{x.Name}\\\"").Join(", ");

        var joinExpression = isMultiTenanted
            ? $"{_tempTable}.id = {storageTable}.id and {_tempTable}.tenant_id = {storageTable}.tenant_id"
            : $"{_tempTable}.id = {storageTable}.id";

        return
            $"insert into {storageTable} ({columns}, {SchemaConstants.LastModifiedColumn}) " +
            $"(select {selectColumns}, transaction_timestamp() " +
            $"from {_tempTable} left join {storageTable} on {joinExpression} " +
            $"where {storageTable}.id is null)";
    }

    public string OverwriteDuplicatesFromTempTable()
    {
        var table = _mapping.Schema.Table;
        var isMultiTenanted = _mapping.TenancyStyle == TenancyStyle.Conjoined;
        var storageTable = table.Identifier.QualifiedName;

        var updates = table.Columns.Where(x => x.Name != "id" && x.Name != SchemaConstants.LastModifiedColumn)
            .Select(x => $"{x.Name} = source.{x.Name}").Join(", ");

        var joinExpression = isMultiTenanted
            ? "source.id = target.id and source.tenant_id = target.tenant_id"
            : "source.id = target.id";

        return
            $"update {storageTable} target SET {updates}, {SchemaConstants.LastModifiedColumn} = transaction_timestamp() FROM {_tempTable} source WHERE {joinExpression}";
    }

    public string CreateTempTableForCopying()
    {
        return $"create temporary table {_tempTable} (like {_mapping.TableName.QualifiedName} including defaults)";
    }
}
