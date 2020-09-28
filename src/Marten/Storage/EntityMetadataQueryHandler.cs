using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Util;

namespace Marten.Storage
{
    internal class EntityMetadataQueryHandler: IQueryHandler<DocumentMetadata>
    {
        private readonly object _id;
        private readonly IDocumentMapping _mapping;
        private readonly MetadataColumn[] _columns;

        public EntityMetadataQueryHandler(object id, IDocumentMapping mapping)
        {
            _id = id;
            _mapping = mapping;

            SourceType = mapping.DocumentType;

            // TODO -- use memoized table on DocumentMapping
            if (mapping is DocumentMapping m)
            {
                _columns = new DocumentTable(m).OfType<MetadataColumn>().ToArray();
            }
            else if (mapping is SubClassMapping s)
            {
                _columns = new DocumentTable(s.Parent).OfType<MetadataColumn>().ToArray();
            }


        }

        public void ConfigureCommand(CommandBuilder sql, IMartenSession session)
        {
            sql.Append("select id, ");

            var fields = _columns.Select(x => x.Name).Join(", ");

            sql.Append(fields);

            sql.Append(" from ");
            sql.Append((string)_mapping.Table.QualifiedName);
            sql.Append(" where id = :id");

            sql.AddNamedParameter("id", _id);
        }

        public DocumentMetadata Handle(DbDataReader reader, IMartenSession session)
        {
            if (!reader.Read())
                return null;

            var id = reader.GetFieldValue<object>(0);
            var metadata = new DocumentMetadata(id);

            for (var i = 0; i < _columns.Length; i++)
            {
                _columns[i].Apply(metadata, i + 1, reader);
            }

            return metadata;
        }

        public async Task<DocumentMetadata> HandleAsync(DbDataReader reader, IMartenSession session,
            CancellationToken token)
        {
            var hasAny = await reader.ReadAsync(token).ConfigureAwait(false);
            if (!hasAny)
                return null;

            var id = await reader.GetFieldValueAsync<object>(0, token);
            var metadata = new DocumentMetadata(id);

            for (var i = 0; i < _columns.Length; i++)
            {
                await _columns[i].ApplyAsync(metadata, i + 1, reader, token);
            }

            return metadata;
        }

        public Type SourceType { get; }
    }
}
