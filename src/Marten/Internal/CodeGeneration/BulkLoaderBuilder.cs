using System.Linq;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Storage;

namespace Marten.Internal.CodeGeneration
{
    public class BulkLoaderBuilder
    {
        private readonly DocumentMapping _mapping;
        private readonly string _tempTable;

        public BulkLoaderBuilder(DocumentMapping mapping)
        {
            _mapping = mapping;
            _tempTable = _mapping.Table.Name + "_temp" ;
        }

        public GeneratedType BuildType(GeneratedAssembly assembly)
        {
            var upsertFunction = new UpsertFunction(_mapping);


            var arguments = upsertFunction.OrderedArguments().Where(x => !(x is CurrentVersionArgument)).ToArray();
            var columns = arguments.Select(x => $"\\\"{x.Column}\\\"").Join(", ");

            var type = assembly.AddType($"{_mapping.DocumentType.Name.Sanitize()}BulkLoader",
                typeof(BulkLoader<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType));

            if (_mapping.IsHierarchy())
            {
                type.AllInjectedFields.Add(new InjectedField(typeof(DocumentMapping), "mapping"));
            }

            type.MethodFor("MainLoaderSql").Frames
                .Return($"COPY {_mapping.Table.QualifiedName}({columns}) FROM STDIN BINARY");

            type.MethodFor("TempLoaderSql").Frames
                .Return($"COPY {_tempTable}({columns}) FROM STDIN BINARY");

            type.MethodFor(nameof(CopyNewDocumentsFromTempTable))
                .Frames.Return(CopyNewDocumentsFromTempTable());

            type.MethodFor(nameof(OverwriteDuplicatesFromTempTable))
                .Frames.Return(OverwriteDuplicatesFromTempTable());

            type.MethodFor(nameof(CreateTempTableForCopying))
                .Frames.Return(CreateTempTableForCopying().Replace("\"", "\\\""));

            var load = type.MethodFor("LoadRow");

            for (int i = 0; i < arguments.Length; i++)
            {
                arguments[i].GenerateBulkWriterCode(type, load, _mapping);
            }

            return type;
        }

        public string CopyNewDocumentsFromTempTable()
        {
            var table = new DocumentTable(_mapping);

            var storageTable = table.Identifier.QualifiedName;
            var columns = table.Where(x => x.Name != DocumentMapping.LastModifiedColumn).Select(x => $"\\\"{x.Name}\\\"").Join(", ");
            var selectColumns = table.Where(x => x.Name != DocumentMapping.LastModifiedColumn).Select(x => $"{_tempTable}.\\\"{x.Name}\\\"").Join(", ");

            return $"insert into {storageTable} ({columns}, {DocumentMapping.LastModifiedColumn}) (select {selectColumns}, transaction_timestamp() from {_tempTable} left join {storageTable} on {_tempTable}.id = {storageTable}.id where {storageTable}.id is null)";
        }

        public string OverwriteDuplicatesFromTempTable()
        {
            var table = new DocumentTable(_mapping);
            var storageTable = table.Identifier.QualifiedName;

            var updates = table.Where(x => x.Name != "id" && x.Name != DocumentMapping.LastModifiedColumn)
                .Select(x => $"{x.Name} = source.{x.Name}").Join(", ");

            return $@"update {storageTable} target SET {updates}, {DocumentMapping.LastModifiedColumn} = transaction_timestamp() FROM {_tempTable} source WHERE source.id = target.id";
        }

        public string CreateTempTableForCopying()
        {
            return $"create temporary table {_tempTable} as select * from {_mapping.Table.QualifiedName};truncate table {_tempTable}";
        }


    }
}
