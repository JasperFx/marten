using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;
using Marten.Schema;
using Marten.Util;

namespace Marten.Storage.Metadata
{
    internal class EntityMetadataQueryHandler: IQueryHandler<DocumentMetadata>
    {
        private readonly object _id;
        private readonly IDocumentStorage _storage;
        private readonly MetadataColumn[] _columns;

        public EntityMetadataQueryHandler(object id, IDocumentStorage storage)
        {
            _id = id;
            _storage = storage;

            SourceType = storage.DocumentType;

            if (storage.Fields is DocumentMapping m)
            {
                _columns = m.Schema.Table.Columns.OfType<MetadataColumn>().ToArray();
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(storage));
            }
        }

        public void ConfigureCommand(CommandBuilder sql, IMartenSession session)
        {
            sql.Append("select id, ");

            var fields = _columns.Select(x => x.Name).Join(", ");

            sql.Append(fields);

            sql.Append(" from ");
            sql.Append(_storage.TableName.QualifiedName);
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
                _columns[i].Apply(session, metadata, i + 1, reader);
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
                await _columns[i].ApplyAsync(session, metadata, i + 1, reader, token);
            }

            return metadata;
        }

        public Type SourceType { get; }
    }
}
