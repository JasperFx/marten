using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services;
using Marten.Util;

namespace Marten.Storage
{
    public class EntityMetadataQueryHandler: IQueryHandler<DocumentMetadata>
    {
        private readonly Dictionary<string, int> _fields;
        private readonly object _id;
        private readonly IDocumentMapping _mapping;
        private readonly IDocumentStorage _storage;

        public EntityMetadataQueryHandler(object entity, IDocumentStorage storage, IDocumentMapping mapping)
        {
            _id = storage.Identity(entity);
            _storage = storage;
            _mapping = mapping;

            var fieldIndex = 0;
            _fields = new Dictionary<string, int>
            {
                {DocumentMapping.VersionColumn, fieldIndex++},
                {DocumentMapping.LastModifiedColumn, fieldIndex++},
                {DocumentMapping.DotNetTypeColumn, fieldIndex++}
            };

            var queryableDocument = _mapping.ToQueryableDocument();
            if (queryableDocument.SelectFields().Contains(DocumentMapping.DocumentTypeColumn))
                _fields.Add(DocumentMapping.DocumentTypeColumn, fieldIndex++);
            if (queryableDocument.DeleteStyle == DeleteStyle.SoftDelete)
            {
                _fields.Add(DocumentMapping.DeletedColumn, fieldIndex++);
                _fields.Add(DocumentMapping.DeletedAtColumn, fieldIndex++);
            }

            if (_mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                _fields.Add(TenantIdColumn.Name, fieldIndex);
            }
        }

        public void ConfigureCommand(CommandBuilder sql)
        {
            sql.Append("select ");

            var fields = _fields.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToArray();

            sql.Append(fields[0]);
            for (var i = 1; i < fields.Length; i++)
            {
                sql.Append(", ");
                sql.Append(fields[i]);
            }

            sql.Append(" from ");
            sql.Append((string)_mapping.Table.QualifiedName);
            sql.Append(" where id = :id");

            sql.AddNamedParameter("id", _id);
        }

        public Type SourceType => _storage.DocumentType;

        public DocumentMetadata Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            if (!reader.Read())
                return null;

            var version = reader.GetFieldValue<Guid>(0);
            var timestamp = reader.GetFieldValue<DateTime>(1);
            var dotNetType = reader.GetFieldValue<string>(2);
            var docType = GetOptionalFieldValue<string>(reader, DocumentMapping.DocumentTypeColumn);
            var deleted = GetOptionalFieldValue<bool>(reader, DocumentMapping.DeletedColumn);
            var deletedAt = GetOptionalFieldValue<DateTime>(reader, DocumentMapping.DeletedAtColumn);
            var tenantId = GetOptionalFieldValue<string>(reader, TenantIdColumn.Name);

            var metadata = new DocumentMetadata(timestamp, version, dotNetType, docType, deleted, deletedAt)
            {
                TenantId = tenantId ?? Tenancy.DefaultTenantId
            };

            return metadata;
        }

        public async Task<DocumentMetadata> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats,
            CancellationToken token)
        {
            var hasAny = await reader.ReadAsync(token).ConfigureAwait(false);
            if (!hasAny)
                return null;

            var version = await reader.GetFieldValueAsync<Guid>(0, token).ConfigureAwait(false);
            var timestamp = await reader.GetFieldValueAsync<DateTime>(1, token).ConfigureAwait(false);
            var dotNetType = await reader.GetFieldValueAsync<string>(2, token).ConfigureAwait(false);
            var docType = await GetOptionalFieldValueAsync<string>(reader, DocumentMapping.DocumentTypeColumn, token)
                .ConfigureAwait(false);
            var deleted = await GetOptionalFieldValueAsync<bool>(reader, DocumentMapping.DeletedColumn, token)
                .ConfigureAwait(false);
            var deletedAt =
                await GetOptionalFieldValueAsync<DateTime>(reader, DocumentMapping.DeletedAtColumn, token)
                    .ConfigureAwait(false);

            var metadata = new DocumentMetadata(timestamp, version, dotNetType, docType, deleted, deletedAt);
            if (_mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                metadata.TenantId = await GetOptionalFieldValueAsync<string>(reader, TenantIdColumn.Name, token)
                    .ConfigureAwait(false);
            }

            return metadata;
        }

        private T GetOptionalFieldValue<T>(DbDataReader reader, string fieldName)
        {
            if (_fields.TryGetValue(fieldName, out int ordinal) && !reader.IsDBNull(ordinal))
                return reader.GetFieldValue<T>(ordinal);
            return default;
        }

        private T? GetOptionalFieldValue<T>(DbDataReader reader, string fieldName, T? defaultValue) where T : struct
        {
            if (_fields.TryGetValue(fieldName, out int ordinal) && !reader.IsDBNull(ordinal))
                return reader.GetFieldValue<T>(ordinal);
            return defaultValue;
        }

        private async Task<T> GetOptionalFieldValueAsync<T>(DbDataReader reader, string fieldName,
            CancellationToken token)
        {
            if (_fields.TryGetValue(fieldName, out int ordinal) &&
                !await reader.IsDBNullAsync(ordinal, token).ConfigureAwait(false))
                return await reader.GetFieldValueAsync<T>(ordinal, token).ConfigureAwait(false);
            return default;
        }

        private async Task<T?> GetOptionalFieldValueAsync<T>(DbDataReader reader, string fieldName, T? defaultValue,
            CancellationToken token) where T : struct
        {
            if (_fields.TryGetValue(fieldName, out int ordinal) &&
                !await reader.IsDBNullAsync(ordinal, token).ConfigureAwait(false))
                return await reader.GetFieldValueAsync<T>(ordinal, token).ConfigureAwait(false);
            return defaultValue;
        }
    }
}
