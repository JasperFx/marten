using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Schema.BulkLoading;
using Marten.Services;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using ReflectionExtensions = LamarCodeGeneration.ReflectionExtensions;

namespace Marten.V4Internals
{
    public class BulkLoaderBuilder
    {
        private readonly DocumentMapping _mapping;
        private readonly DbObjectName _tempTable;

        public BulkLoaderBuilder(DocumentMapping mapping)
        {
            _mapping = mapping;
            _tempTable = new DbObjectName(_mapping.Table.Schema, _mapping.Table.Name + "_temp") ;
        }

        public GeneratedType BuildType(GeneratedAssembly assembly)
        {
            var upsertFunction = new UpsertFunction(_mapping);


            var arguments = upsertFunction.OrderedArguments().Where(x => !(x is CurrentVersionArgument)).ToArray();
            var columns = arguments.Select(x => $"\\\"{x.Column}\\\"").Join(", ");

            var type = assembly.AddType($"{_mapping.DocumentType.Name}BulkLoader",
                typeof(BulkLoader<>).MakeGenericType(_mapping.DocumentType));

            if (_mapping.IsHierarchy())
            {
                type.AllInjectedFields.Add(new InjectedField(typeof(DocumentMapping), "mapping"));
            }

            type.MethodFor("MainLoaderSql").Frames
                .Return($"COPY {_mapping.Table.QualifiedName}({columns}) FROM STDIN BINARY");

            type.MethodFor("TempLoaderSql").Frames
                .Return($"COPY {_tempTable.QualifiedName}({columns}) FROM STDIN BINARY");

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

    public interface IBulkLoader<T>
    {
        void Load(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents, CharArrayTextWriter pool);

        string CreateTempTableForCopying();

        void LoadIntoTempTable(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents, CharArrayTextWriter pool);

        string CopyNewDocumentsFromTempTable();

        string OverwriteDuplicatesFromTempTable();
    }


    public abstract class BulkLoader<T>: IBulkLoader<T>
    {
        public void Load(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents,
            CharArrayTextWriter pool)
        {
            using (var writer = conn.BeginBinaryImport(MainLoaderSql()))
            {
                foreach (var document in documents)
                {
                    writer.StartRow();
                    LoadRow(writer, document, tenant, serializer, pool);
                }

                writer.Complete();
            }
        }

        public abstract void LoadRow(NpgsqlBinaryImporter writer, T document, ITenant tenant, ISerializer serializer,
            CharArrayTextWriter pool);


        public abstract string MainLoaderSql();
        public abstract string TempLoaderSql();



        public abstract string CreateTempTableForCopying();

        public void LoadIntoTempTable(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents,
            CharArrayTextWriter pool)
        {
            using (var writer = conn.BeginBinaryImport(TempLoaderSql()))
            {
                foreach (var document in documents)
                {
                    writer.StartRow();
                    LoadRow(writer, document, tenant, serializer, pool);
                }

                writer.Complete();
            }
        }

        public abstract string CopyNewDocumentsFromTempTable();

        public abstract string OverwriteDuplicatesFromTempTable();
    }
}
