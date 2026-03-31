using System.Collections.Generic;
using System.Linq;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Storage;
using NpgsqlTypes;

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


        var mainArguments = orderArgumentsForBulkWriting(upsertFunction, includeExpectedVersion: false);
        var tempArguments = orderArgumentsForBulkWriting(upsertFunction, includeExpectedVersion: true);

        var mainColumns = mainArguments.Select(x => $"\\\"{x.Column}\\\"").Join(", ");
        var tempColumns = tempArguments.Select(x => $"\\\"{x.Column}\\\"").Join(", ");

        var type = assembly.AddType(TypeName,
            typeof(BulkLoader<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType));

        if (_mapping.IsHierarchy())
        {
            type.AllInjectedFields.Add(new InjectedField(typeof(DocumentMapping), "mapping"));
        }

        type.MethodFor("MainLoaderSql")
            .Frames
            .ReturnNewStringConstant("MAIN_LOADER_SQL",
                $"COPY {_mapping.TableName.QualifiedName}({mainColumns}) FROM STDIN BINARY");

        type.MethodFor("TempLoaderSql").Frames
            .ReturnNewStringConstant("TEMP_LOADER_SQL", $"COPY {_tempTable}({tempColumns}) FROM STDIN BINARY");

        type.MethodFor(nameof(CopyNewDocumentsFromTempTable))
            .Frames.ReturnNewStringConstant("COPY_NEW_DOCUMENTS_SQL", CopyNewDocumentsFromTempTable());

        type.MethodFor(nameof(OverwriteDuplicatesFromTempTable))
            .Frames.ReturnNewStringConstant("OVERWRITE_SQL", OverwriteDuplicatesFromTempTable());

        type.MethodFor(nameof(OverwriteDuplicatesFromTempTableWithVersionCheck))
            .Frames.ReturnNewStringConstant("OVERWRITE_WITH_VERSION_SQL", OverwriteDuplicatesFromTempTableWithVersionCheck());

        type.MethodFor(nameof(CreateTempTableForCopying))
            .Frames.ReturnNewStringConstant("CREATE_TEMP_TABLE_FOR_COPYING_SQL",
                CreateTempTableForCopying().Replace("\"", "\\\""));

        var loadAsync = type.MethodFor("LoadRowAsync");

        foreach (var argument in mainArguments)
        {
            argument.GenerateBulkWriterCodeAsync(type, loadAsync, _mapping);
        }

        var loadTempAsync = type.MethodFor("LoadTempRowAsync");

        foreach (var argument in tempArguments)
        {
            argument.GenerateBulkWriterCodeAsync(type, loadTempAsync, _mapping);
        }

        return type;
    }

    private List<UpsertArgument> orderArgumentsForBulkWriting(UpsertFunction upsertFunction, bool includeExpectedVersion)
    {
        var arguments = upsertFunction.OrderedArguments().Where(x => x is not CurrentVersionArgument).ToList();
        // You need the document body to go last so that any metadata pushed into the document
        // is serialized into the JSON data
        var body = arguments.OfType<DocJsonBodyArgument>().Single();
        arguments.Remove(body);

        if (includeExpectedVersion && needsExpectedVersion())
        {
            var dbType = _mapping.UseNumericRevisions ? NpgsqlDbType.Integer : NpgsqlDbType.Uuid;
            var expectedArgument = new ExpectedVersionArgument(dbType);
            var insertIndex = arguments.FindIndex(x => x is VersionArgument || x is RevisionArgument);
            if (insertIndex < 0)
            {
                insertIndex = arguments.Count;
            }

            arguments.Insert(insertIndex, expectedArgument);
        }

        arguments.Add(body);
        return arguments;
    }

    public string CopyNewDocumentsFromTempTable()
    {
        var table = _mapping.Schema.Table;

        var storageTable = table.Identifier.QualifiedName;
        var columns = table.Columns.Where(x => x.Name != SchemaConstants.LastModifiedColumn)
            .Select(x => $"\\\"{x.Name}\\\"").Join(", ");
        var selectColumns = table.Columns.Where(x => x.Name != SchemaConstants.LastModifiedColumn)
            .Select(x => $"{_tempTable}.\\\"{x.Name}\\\"").Join(", ");

        var joinExpression = buildPrimaryKeyJoinExpression(table, _tempTable, storageTable);

        // Use the first PK column for the NULL check (any PK column works since they're all NOT NULL)
        var firstPkColumn = table.PrimaryKeyColumns.First();

        return
            $"insert into {storageTable} ({columns}, {SchemaConstants.LastModifiedColumn}) " +
            $"(select {selectColumns}, transaction_timestamp() " +
            $"from {_tempTable} left join {storageTable} on {joinExpression} " +
            $"where {storageTable}.{firstPkColumn} is null)";
    }

    public string OverwriteDuplicatesFromTempTable()
    {
        var table = _mapping.Schema.Table;
        var storageTable = table.Identifier.QualifiedName;

        var pkColumns = table.PrimaryKeyColumns.ToArray();
        var updates = table.Columns
            .Where(x => !pkColumns.Contains(x.Name) && x.Name != SchemaConstants.LastModifiedColumn)
            .Select(x => $"{x.Name} = source.{x.Name}").Join(", ");

        var joinExpression = buildPrimaryKeyJoinExpression(table, "source", "target");

        return
            $"update {storageTable} target SET {updates}, {SchemaConstants.LastModifiedColumn} = transaction_timestamp() FROM {_tempTable} source WHERE {joinExpression}";
    }

    public string OverwriteDuplicatesFromTempTableWithVersionCheck()
    {
        if (!needsExpectedVersion())
        {
            return OverwriteDuplicatesFromTempTable();
        }

        var table = _mapping.Schema.Table;
        var storageTable = table.Identifier.QualifiedName;

        var pkColumns = table.PrimaryKeyColumns.ToArray();
        var updates = table.Columns
            .Where(x => !pkColumns.Contains(x.Name) && x.Name != SchemaConstants.LastModifiedColumn)
            .Select(x => $"{x.Name} = source.{x.Name}").Join(", ");

        var joinExpression = buildPrimaryKeyJoinExpression(table, "source", "target");

        return $"update {storageTable} target SET {updates}, {SchemaConstants.LastModifiedColumn} = transaction_timestamp() FROM {_tempTable} source WHERE {joinExpression} and target.{SchemaConstants.VersionColumn} = source.{SchemaConstants.ExpectedVersionColumn}";
    }

    /// <summary>
    /// Build a join expression using all primary key columns from the table.
    /// This handles composite PKs from partitioned tables where partition columns
    /// are part of the primary key.
    /// </summary>
    private static string buildPrimaryKeyJoinExpression(Weasel.Postgresql.Tables.Table table, string leftAlias, string rightAlias)
    {
        return table.PrimaryKeyColumns
            .Select(col => $"{leftAlias}.{col} = {rightAlias}.{col}")
            .Join(" and ");
    }

    public string CreateTempTableForCopying()
    {
        if (!needsExpectedVersion())
        {
            return $"create temporary table {_tempTable} (like {_mapping.TableName.QualifiedName} including defaults)";
        }

        var expectedType = _mapping.UseNumericRevisions ? "integer" : "uuid";
        return $"create temporary table {_tempTable} (like {_mapping.TableName.QualifiedName} including defaults, \"{SchemaConstants.ExpectedVersionColumn}\" {expectedType})";
    }

    private bool needsExpectedVersion()
    {
        return _mapping.Metadata.Version.Enabled || _mapping.Metadata.Revision.Enabled || _mapping.UseOptimisticConcurrency || _mapping.UseNumericRevisions;
    }
}
