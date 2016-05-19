using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema.Identity;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema.BulkLoading
{
    public class BulkLoader<T> : IBulkLoader<T>
    {
        private readonly DocumentMapping _mapping;
        private readonly IdAssignment<T> _assignment;
        private readonly string _sql;

        private readonly Action<T, string, ISerializer, NpgsqlBinaryImporter> _transferData;
        private readonly string _baseSql;


        public BulkLoader(ISerializer serializer, DocumentMapping mapping, IdAssignment<T> assignment)
        {
            _mapping = mapping;
            _assignment = assignment;
            var upsertFunction = new UpsertFunction(mapping);


            var writer = Expression.Parameter(typeof(NpgsqlBinaryImporter), "writer");
            var document = Expression.Parameter(typeof(T), "document");
            var alias = Expression.Parameter(typeof(string), "alias");
            var serializerParam = Expression.Parameter(typeof(ISerializer), "serializer");

            var arguments = upsertFunction.OrderedArguments().ToArray();
            var expressions = arguments.Select(x => x.CompileBulkImporter(serializer.EnumStorage, writer, document, alias, serializerParam));

            var columns = arguments.Select(x => $"\"{x.Column}\"").Join(", ");
            _baseSql = $"COPY %TABLE%({columns}) FROM STDIN BINARY";
            _sql = _baseSql.Replace("%TABLE%", mapping.Table.QualifiedName);

            var block = Expression.Block(expressions);

            var lambda = Expression.Lambda<Action<T, string, ISerializer, NpgsqlBinaryImporter>>(block, document, alias, serializerParam, writer);

            _transferData = lambda.Compile();
        }

        public void Load(ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents)
        {
            load(serializer, conn, documents, _sql);
        }

        public void Load(TableName table, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents)
        {
            var sql = _baseSql.Replace("%TABLE%", table.QualifiedName);
            load(serializer, conn, documents, sql);
        }

        private void load(ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents, string sql)
        {
            using (var writer = conn.BeginBinaryImport(sql))
            {
                foreach (var document in documents)
                {
                    var assigned = false;
                    _assignment.Assign(document, out assigned);

                    writer.StartRow();

                    _transferData(document, _mapping.AliasFor(document.GetType()), serializer, writer);
                }
            }
        }
    }
}