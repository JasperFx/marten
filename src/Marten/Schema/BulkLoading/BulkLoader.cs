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
        private readonly IdAssignment<T> _assignment;
        private readonly string _sql;

        private readonly Action<T, NpgsqlBinaryImporter> _transferData;


        public BulkLoader(DocumentMapping mapping, IdAssignment<T> assignment)
        {
            _assignment = assignment;
            var upsertFunction = mapping.ToUpsertFunction();


            var writer = Expression.Parameter(typeof(NpgsqlBinaryImporter), "writer");
            var document = Expression.Parameter(typeof(T), "document");

            var arguments = upsertFunction.OrderedArguments().Where(x => x.Members != null && x.Members.Any()).ToArray();
            var expressions = arguments.Select(x => x.CompileBulkImporter<T>(writer, document));

            var columns = arguments.Select(x => $"\"{x.Column}\"").Join(", ");
            _sql = $"COPY {mapping.Table.QualifiedName}(data, {columns}) FROM STDIN BINARY";

            var block = Expression.Block(expressions);

            var lambda = Expression.Lambda<Action<T, NpgsqlBinaryImporter>>(block, document, writer);

            _transferData = lambda.Compile();
        }

        public void Load(ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents)
        {
            using (var writer = conn.BeginBinaryImport(_sql))
            {
                foreach (var document in documents)
                {
                    var assigned = false;
                    _assignment.Assign(document, out assigned);

                    writer.StartRow();

                    writer.Write(serializer.ToJson(document), NpgsqlDbType.Jsonb);

                    _transferData(document, writer);
                }
            }
        }
    }
}